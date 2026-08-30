// board1
#define MES_MAX 3

const int message[MES_MAX] = {0, 0, 0};

void setup() {
  Serial.begin(9600);
}

void loop() {
  if (Serial.available() > 0) {
    String data = Serial.readStringUntil('\n');

    int values[MES_MAX];
    int index = 0;
    
    Serial.print("I received: ");
    Serial.println(data);
    
    char* token = strtok((char*)data.c_str(), ",");

    while (token != NULL && index < MES_MAX) {
      values[index] = constrain(atoi(token), 0, 255);
      token = strtok(NULL, ",");
      index++;
    }
  }
}