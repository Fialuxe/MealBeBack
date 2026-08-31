/*
  プロトコル: CSV + 改行 ("a,b,c\n")
  - Unity -> Arduino: 受信して行動に反映
  - Arduino -> Unity: 一定間隔で状態を送信

  delay()は使わない。全処理をmillis()ベースの非ブロッキングにすること。
*/

const long BAUD_RATE = 9600;              // Unity側SerialPortと一致させる
const unsigned long SEND_INTERVAL_MS = 100;

int windowType = 0; // 0:idle, 1:chewing, 2:notChewing（WoZ入力等で更新）

String inputBuffer = "";
unsigned long lastSendAt = 0;

void setup() {
  Serial.begin(BAUD_RATE);
  // モーター等の初期化はここに追加
}

void loop() {
  readFromUnity();

  if (millis() - lastSendAt >= SEND_INTERVAL_MS) {
    lastSendAt = millis();
    sendToUnity();
  }

  // モーター駆動など他の処理もここに非ブロッキングで実装する
}

void readFromUnity() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    if (c == '\n') {
      handleLine(inputBuffer);
      inputBuffer = "";
    } else if (c != '\r') {
      inputBuffer += c;
    }
  }
}

void handleLine(const String &line) {
  int idx1 = line.indexOf(',');
  int idx2 = line.indexOf(',', idx1 + 1);
  if (idx1 < 0 || idx2 < 0) return; // 不正な行は無視して継続（止めない）

  int userChoice = line.substring(0, idx1).toInt();
  int isCorrect  = line.substring(idx1 + 1, idx2).toInt();
  int recvWindow = line.substring(idx2 + 1).toInt();

  // TODO: userChoice / isCorrect / recvWindow を使ってモーター等を駆動
  (void)userChoice; (void)isCorrect; (void)recvWindow;
}

void sendToUnity() {
  Serial.print(0);   // TODO: 実際に送りたい値に置き換え
  Serial.print(',');
  Serial.print(0);   // TODO
  Serial.print(',');
  Serial.print(windowType);
  Serial.print('\n');
}
