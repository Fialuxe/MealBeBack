/// <scriptName>FishRotator.cs</scriptName>
/// <Author>Fialuxe</Author>
/// <Version>0.0.1</Version>
/// <Description>X軸周りで魚を回転させる</Description>
/// <Usage>X軸周りで魚を回転させるために使用します。</Usage>
/// <Parameters>
/// <Parameter name="duration" type="float" description="回転にかかる時間（秒）" />
/// <Parameter name="rotationAngle" type="float" description="回転する総角度（度）" />
/// <Parameter name="direction" type="RotationDirection" description="回転方向" />
/// </Parameters>

using System.Collections;
using UnityEngine;

public class CustomAxisRotator : MonoBehaviour
{
    public enum RotationDirection
    {
        Clockwise,
        CounterClockwise
    }

    [Header("Rotation Settings")]
    [Tooltip("回転にかかる時間（秒）")]
    public float duration = 2.0f;

    [Tooltip("回転する総角度（度）")]
    public float rotationAngle = 360f;

    [Tooltip("回転方向")]
    public RotationDirection direction = RotationDirection.Clockwise;

    [Tooltip("回転軸（ローカル座標系）: X=(1,0,0), Y=(0,1,0), Z=(0,0,1)")]
    public Vector3 rotationAxis = Vector3.right;

    void Start()
    {
        // 動作確認のための呼び出し
        StartCoroutine(RotateOverTime(duration, rotationAngle, direction, rotationAxis));
    }

    /// <summary>
    /// 指定された時間、角度、方向、軸でローカル回転を行うコルーチン
    /// </summary>
    public IEnumerator RotateOverTime(float time, float angle, RotationDirection dir, Vector3 axis)
    {
        if (time <= 0f)
        {
            Debug.LogWarning("時間は0より大きい値を指定してください。");
            yield break;
        }

        // 入力された軸ベクトルを正規化（長さ1の方向のみのベクトルにする）
        Vector3 normalizedAxis = axis.normalized;
        
        // 全て0のベクトルが渡された場合のエラーハンドリング
        if (normalizedAxis == Vector3.zero)
        {
            Debug.LogError("回転軸のベクトルが無効（0, 0, 0）です。");
            yield break;
        }

        float elapsedTime = 0f;
        float actualAngle = (dir == RotationDirection.Clockwise) ? angle : -angle;

        while (elapsedTime < time)
        {
            float deltaTime = Time.deltaTime;

            if (elapsedTime + deltaTime > time)
            {
                deltaTime = time - elapsedTime;
            }

            float stepAngle = (actualAngle / time) * deltaTime;
            
            // X軸固定だった箇所を、正規化済みの指定軸(normalizedAxis)に変更
            transform.Rotate(normalizedAxis, stepAngle, Space.Self);

            elapsedTime += deltaTime;
            yield return null;
        }
    }
}