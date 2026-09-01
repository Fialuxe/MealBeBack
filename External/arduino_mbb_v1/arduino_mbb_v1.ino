/*
  Meal Be Back で利用するデバイス (充填 / 吸引) の制御スクリプト。
  仕様: https://github.com/Fialuxe/MealBeBack/issues/42
  DRV8835 + Arduino <-> Unity 非ブロッキング制御
  ------------------------------------------------
  Unity -> Arduino : "(<状態>,<値>)\n"
    'f' = 中の量を <値>% まで「増やす」 (既に <値> 以上なら何もしない)
    's' = 中の量を <値>% まで「減らす」 (既に <値> 以下なら何もしない)
    'c' = モーターを回さず currentInside を <値>% に設定する (状態同期用)
    'i' = 即停止 (<値> は無視)
    <値> : 0-100 の整数。範囲外の f / s / c は無視する。

  Arduino -> Unity : "(<処理状態>,<currentInside>)\n"   (SEND_INTERVAL_MS ごと)
    処理状態      : 1 = 駆動中 / 0 = 停止中
    currentInside : 現在の充填率 0-100

  起動直後は「デバイスは 100% 充填済み」を前提に currentInside = 100 で始まる。
  実際の初期量が違う場合は Unity から "(c,<実量>)" を送って合わせる。

  Aチャンネル(AIN1/AIN2) = fillモーター(押す)
  Bチャンネル(BIN1/BIN2) = suckモーター(引く)
  MODE=LOW固定(PHASE/ENABLE) : IN1=PHASE(方向), IN2=ENABLE(有効/無効)

  実装方針 (最小):
    ・目標値との差 (%) ぶんだけモーターを回す時間を決めて回す (loop は止めない)。
    ・時間に到達したら currentInside を目標値にして停止。
    ・途中で別コマンドが来たら、進行中のぶんは反映せず新しい目標へ切り替える。
      → 動作中に指令を重ねると currentInside と実物がズレる。idle((0,x)) を待ってから次を送ること。
*/

// ==== ピン定義 ====
const int AIN1 = 5;   // fillモーター PHASE
const int AIN2 = 6;   // fillモーター ENABLE
const int BIN1 = 9;   // suckモーター PHASE
const int BIN2 = 10;  // suckモーター ENABLE
const int MODE = 4;   // PHASE/ENABLEモード固定

// ==== 通信設定 ====
const long BAUD_RATE = 9600;
const unsigned long SEND_INTERVAL_MS = 50; // 状態送信の周期

// ==== 1%あたりの駆動時間(実測して調整する) ====
const unsigned long MS_PER_PERCENT = 10;

// ==== 状態管理 ====
enum MotorState { MOTOR_STOPPED, MOTOR_FILLING, MOTOR_SUCKING };

MotorState motorState = MOTOR_STOPPED;

int currentInside = 100;             // 0-100。アクション完了時のみ更新する
int actionTargetInside = 0;          // 進行中アクション完了後の currentInside
unsigned long actionStartMillis = 0; // 進行中アクションの開始時刻
unsigned long actionDurationMs = 0;  // 進行中アクションの駆動時間

char lineBuf[16];   // 受信行バッファ("(f,100)" が入れば十分)
int lineLen = 0;
unsigned long lastSendAt = 0;

void setup() {
  Serial.begin(BAUD_RATE);

  pinMode(AIN1, OUTPUT);
  pinMode(AIN2, OUTPUT);
  pinMode(BIN1, OUTPUT);
  pinMode(BIN2, OUTPUT);
  pinMode(MODE, OUTPUT);
  digitalWrite(MODE, LOW);

  stopMotors();
}

void loop() {
  readSerial();
  updateMotion();

  if (millis() - lastSendAt >= SEND_INTERVAL_MS) {
    lastSendAt = millis();
    sendStatus();
  }
}

// ============================================================
// シリアル受信(非ブロッキング)。'\n' で 1 行確定
// ============================================================
void readSerial() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();

    if (c == '\n') {
      lineBuf[lineLen] = '\0';
      handleLine(lineBuf);
      lineLen = 0;
    } else if (c == '\r') {
      // 無視
    } else if (lineLen < (int)sizeof(lineBuf) - 1) {
      lineBuf[lineLen++] = c;
    } else {
      lineLen = 0; // 想定外に長い行はまるごと捨てて継続
    }
  }
}

// 期待フォーマット: "(f,80)" 。'(' は飛ばし、',' の後ろを数値として読む
void handleLine(const char *line) {
  const char *p = line;
  if (*p == '(') p++;

  char cmd = *p;
  const char *comma = strchr(p, ',');
  if (comma == NULL) return;          // ',' が無い行は無視

  int value = atoi(comma + 1);        // "80)" -> 80

  switch (cmd) {
    case 'i':
      stopMotors();
      motorState = MOTOR_STOPPED;
      break;

    case 'c':
      if (value < 0 || value > 100) return;
      stopMotors();
      motorState = MOTOR_STOPPED;
      currentInside = value;          // モーターは回さず現在値だけ書き換える
      break;

    case 'f':
      if (value < 0 || value > 100) return;
      moveToward(MOTOR_FILLING, value);
      break;

    case 's':
      if (value < 0 || value > 100) return;
      moveToward(MOTOR_SUCKING, value);
      break;

    default:
      return; // 未定義コマンドは無視
  }
}

// ============================================================
// currentInside を target に近づける。
// dir が示す向き (FILLING=増やす / SUCKING=減らす) にしか動かさない。
// ============================================================
void moveToward(MotorState dir, int target) {
  int delta = target - currentInside;

  // 指定の向きと逆、または既に到達済みなら何もしない
  if (dir == MOTOR_FILLING && delta <= 0) { stopMotors(); motorState = MOTOR_STOPPED; return; }
  if (dir == MOTOR_SUCKING && delta >= 0) { stopMotors(); motorState = MOTOR_STOPPED; return; }

  motorState = dir;
  actionTargetInside = target;
  actionStartMillis = millis();
  actionDurationMs = (unsigned long)abs(delta) * MS_PER_PERCENT;

  driveMotor(dir);
}

// 駆動時間に到達したら目標値へ更新して停止する
void updateMotion() {
  if (motorState == MOTOR_STOPPED) return;

  if (millis() - actionStartMillis >= actionDurationMs) {
    currentInside = constrain(actionTargetInside, 0, 100);
    stopMotors();
    motorState = MOTOR_STOPPED;
  }
}

void driveMotor(MotorState state) {
  if (state == MOTOR_FILLING) {
    digitalWrite(AIN1, LOW);
    digitalWrite(AIN2, HIGH);
    digitalWrite(BIN1, HIGH);
    digitalWrite(BIN2, LOW);
  } else if (state == MOTOR_SUCKING) {
    digitalWrite(AIN1, HIGH);
    digitalWrite(AIN2, LOW);
    digitalWrite(BIN1, LOW);
    digitalWrite(BIN2, HIGH);
  }
}

void stopMotors() {
  digitalWrite(AIN1, LOW);
  digitalWrite(AIN2, LOW);
  digitalWrite(BIN1, LOW);
  digitalWrite(BIN2, LOW);
}

void sendStatus() {
  Serial.print('(');
  Serial.print(motorState == MOTOR_STOPPED ? 0 : 1);
  Serial.print(',');
  Serial.print(currentInside);
  Serial.print(')');
  Serial.print('\n');
}
