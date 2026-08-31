/*
  このスクリプトは、Meal Be Backにて利用されるデバイスの制御のためのスクリプトです。
  仕様については、https://github.com/Fialuxe/MealBeBack/issues/42 を確認してください。
  DRV8835 + Arduino <-> Unity 非ブロッキング制御
  ------------------------------------------------
  Unity -> Arduino: "(<状態>,<率>)\n"
    状態: 'f'=fill / 's'=suck / 'i'=invalid(全停止・率は無視)
    率  : 0-100の整数。範囲外(負 or 100超)のf/sは丸ごと破棄する
  Arduino -> Unity: "(<処理状態>,<currentInside>)\n"
    処理状態: 1=ongoing(駆動中) / 0=available(停止中)

  Aチャンネル(AIN1/AIN2) = fillモーター(押す)
  Bチャンネル(BIN1/BIN2) = suckモーター(引く)
  MODE=LOW固定(PHASE/ENABLE) : IN1=PHASE(方向), IN2=ENABLE(有効/無効)
*/

// ==== ピン定義 ====
const int AIN1 = 5;   // fillモーター PHASE
const int AIN2 = 6;   // fillモーター ENABLE
const int BIN1 = 9;   // suckモーター PHASE
const int BIN2 = 10;  // suckモーター ENABLE
const int MODE = 4;   // PHASE/ENABLEモード固定

// ==== 通信設定 ====
const long BAUD_RATE = 9600;

const unsigned long SEND_INTERVAL_MS = 100; //命令ごとに置かれるインターバル

const size_t INPUT_BUFFER_MAX_LEN = 32; //入力バッファの最大長

// ==== 1%あたりの駆動時間 ====
 
const unsigned long MS_PER_PERCENT = 50; // TODO 実際に測ってここを書く

// ==== 状態管理 ====
enum MotorState { MOTOR_STOPPED, MOTOR_FILLING, MOTOR_SUCKING };

int currentInside = 0; // 0-100。100%超の流入/0%未満の吸引を防止するための状態管理変数

MotorState motorState = MOTOR_STOPPED;
unsigned long actionStartMillis = 0;
unsigned long actionDurationMs = 0;
int actionStartInside = 0;
int actionTargetDelta = 0; // 常に正の値。符号はmotorStateで判断

String inputBuffer = "";
unsigned long lastSendAt = 0;

void setup() {
  Serial.begin(BAUD_RATE);

  pinMode(AIN1, OUTPUT);
  pinMode(AIN2, OUTPUT);
  pinMode(BIN1, OUTPUT);
  pinMode(BIN2, OUTPUT);
  pinMode(MODE, OUTPUT);
  digitalWrite(MODE, LOW);

  disablePins();
}

void loop() {
  readFromUnity();
  updateMotion();

  if (millis() - lastSendAt >= SEND_INTERVAL_MS) { //インターバルを経過していたら
    lastSendAt = millis();
    sendToUnity();
  }
}

// ============================================================
// シリアル受信(非ブロッキング)
// ============================================================
void readFromUnity() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();

    if (c == '\n') {
      handleLine(inputBuffer);
      inputBuffer = "";
    } else if (c != '\r') {
      inputBuffer += c;
      if (inputBuffer.length() > INPUT_BUFFER_MAX_LEN) {
        inputBuffer = ""; // 異常な長文はバッファごと破棄して継続
      }
    }
  }
}

// 期待フォーマット: "(f,40)" 。括弧・クォート・空白の揺れを許容する
void handleLine(const String &rawLine) {
  String line = rawLine; //バッファを壊さないためにコピー
  line.trim(); //前後の空白・改行を削除
  line.replace("(", ""); //()を削除
  line.replace(")", "");

  int commaIdx = line.indexOf(',');
  if (commaIdx < 0) return; // 不正な行は無視して継続

  String statePart = line.substring(0, commaIdx);//"f"とか
  String ratePart = line.substring(commaIdx + 1);//"40"とか

  statePart.trim();
  statePart.replace("'", "");//'f'をfだけにする
  statePart.replace("\"", "");//"f"をfだけにする
  ratePart.trim();

  if (statePart.length() == 0) return;

  char state = statePart.charAt(0);
  int rate = ratePart.toInt(); // 変換失敗時は0扱い

  handleCommand(state, rate);
}

void handleCommand(char state, int rate) {
  switch (state) {
    case 'i':
      stop(); // 率は無視。即座に全停止
      break;

    case 'f':
      if (rate < 0 || rate > 100) return; // 範囲外は破棄
      fill(rate);
      break;

    case 's':
      if (rate < 0 || rate > 100) return; // 範囲外は破棄
      suck(rate);
      break;

    default:
      return; // 未定義文字は無視
  }
}

// ============================================================
// 仕様に従い、percentだけfillを行う(100%超は自動的に切り詰め)
// ============================================================
void fill(int percent) {
  int actualPercent = min(percent, 100 - currentInside);
  if (actualPercent <= 0) return; // 既に満杯、または実質0
  startAction(MOTOR_FILLING, actualPercent);
}

// ============================================================
// 仕様に従い、percentだけsuckを行う(0%未満は自動的に切り詰め)
// ============================================================
void suck(int percent) {
  int actualPercent = min(percent, currentInside);
  if (actualPercent <= 0) return; // 既に空、または実質0
  startAction(MOTOR_SUCKING, actualPercent);
}

// ============================================================
// モーターを停止させる(進行中アクションは経過分をcurrentInsideに反映してから止める)
// ============================================================
void stop() {
  finalizeAction();
}

// ---- 内部処理 ----

void startAction(MotorState newState, int percentToMove) {
  if (motorState != MOTOR_STOPPED) {
    finalizeAction(); // 実行中アクションがあれば進捗を確定させてから切り替える
  }

  motorState = newState;
  actionStartMillis = millis();
  actionStartInside = currentInside;
  actionTargetDelta = percentToMove;
  actionDurationMs = (unsigned long)percentToMove * MS_PER_PERCENT;

  driveMotor(newState);
}

void updateMotion() {
  if (motorState == MOTOR_STOPPED) return;
  if (millis() - actionStartMillis >= actionDurationMs) {
    finalizeAction(); // 目標時間到達 → 全量反映して停止
  }
}
//進行中のモーター駆動アクションを「今この瞬間の状態」で確定させ、
//currentInsideに反映してから停止する関数。
void finalizeAction() {
  if (motorState == MOTOR_STOPPED) return;

  unsigned long elapsed = millis() - actionStartMillis;
  float ratio = (float)elapsed / (float)actionDurationMs;
  if (ratio > 1.0f) ratio = 1.0f;

  int movedAmount = (int)(actionTargetDelta * ratio + 0.5f);

  if (motorState == MOTOR_FILLING) {
    currentInside = constrain(actionStartInside + movedAmount, 0, 100);
  } else if (motorState == MOTOR_SUCKING) {
    currentInside = constrain(actionStartInside - movedAmount, 0, 100);
  }

  disablePins();
  motorState = MOTOR_STOPPED;
}

void driveMotor(MotorState state) {
  if (state == MOTOR_FILLING) {
    digitalWrite(AIN1, LOW);
    digitalWrite(AIN2, HIGH);
    digitalWrite(BIN1, LOW);
    digitalWrite(BIN2, LOW);
  } else if (state == MOTOR_SUCKING) {
    digitalWrite(AIN1, LOW);
    digitalWrite(AIN2, LOW);
    digitalWrite(BIN1, LOW);
    digitalWrite(BIN2, HIGH);
  }
}

void disablePins() {
  digitalWrite(AIN1, LOW);
  digitalWrite(AIN2, LOW);
  digitalWrite(BIN1, LOW);
  digitalWrite(BIN2, LOW);
}

void sendToUnity() {
  int processingState = (motorState == MOTOR_STOPPED) ? 0 : 1;

  Serial.print('(');
  Serial.print(processingState);
  Serial.print(',');
  Serial.print(currentInside); // 今後使いそうな部分: 現在値をとりあえず送信
  Serial.print(')');
  Serial.print('\n');
}