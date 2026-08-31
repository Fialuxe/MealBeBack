// board1
#define MES_MAX 3
#define LED_PIN 11
#define SEND_INTERVAL_MS 500

// Unityから最後に受け取った値を保持する（受信できていることの確認用にそのまま送り返す）
int message[MES_MAX] = {0, 0, 0};

unsigned long lastSend = 0;

void setup() {
  Serial.begin(9600);
  pinMode(LED_PIN, OUTPUT);
}

void loop() {
  // --- 受信 ---
  if (Serial.available() > 0) {
    String data = Serial.readStringUntil('\n');
    data.trim();

    if (data.length() > 0) {
      // 受信を可視化（LEDを一瞬点灯）
      digitalWrite(LED_PIN, HIGH);

      // "a,b,c" をパースして message[] を更新
      int index = 0;
      char buf[64];
      data.toCharArray(buf, sizeof(buf));
      char* token = strtok(buf, ",");
      while (token != NULL && index < MES_MAX) {
        message[index] = constrain(atoi(token), 0, 255);
        token = strtok(NULL, ",");
        index++;
      }

      // エコーバック（デバッグ用）
      Serial.print("I received: ");
      Serial.println(data);
    }
  } else {
    digitalWrite(LED_PIN, LOW);
  }

  // --- 定期送信: "a,b,windowType" ---
  // Unity側 SerialPortManager が期待する3フィールド形式で常時送る
  unsigned long now = millis();
  if (now - lastSend >= SEND_INTERVAL_MS) {
    lastSend = now;
    Serial.print(message[0]);
    Serial.print(',');
    Serial.print(message[1]);
    Serial.print(',');
    Serial.println(message[2]); // 3番目が windowType として解釈される
  }
}
