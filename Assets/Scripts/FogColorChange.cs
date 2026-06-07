 using UnityEngine;

  public class DayNightController : MonoBehaviour
  {
      private GameManager _gm;

      private void Start()
      {
          _gm = FindAnyObjectByType<GameManager>();
      }

      // 例: スペースキーで発動
      private void Update()
      {
          if (Input.GetKeyDown(KeyCode.Space))
          {
                // 黄色、10秒かけて変化させる例
              _gm.SetTerrainColor(Color.yellow, duration: 10f);
              _gm.SetFogDensity(RenderSettings.fogDensity * 3f, duration: 10f);
          }
      }
  }