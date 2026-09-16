using System;
using UnityEngine;

public class CaptainsWheel : MonoBehaviour
{
	[SerializeField]
	private float lengthPerRevolution = 1f;

	[SerializeField]
	private float direction = 1f;

	[SerializeField]
	[ReadOnly(ReadOnlyMode.ReadOnly)]
	private float value;

	[SerializeField]
	private FloatEvent valueChanged;

	private float currentAngle;

	public float Value => value;

	public event Action<float, float> ValueChanged;

	private void LateUpdate()
	{
		UpdateValues();
	}

	private void UpdateValues()
	{
		Vector3 vector = base.transform.InverseTransformPoint(Vector3.zero);
		float num = 57.29578f * Mathf.Atan2(vector.y, vector.x);
		float num2 = num - currentAngle;
		if (num2 > 180f)
		{
			num2 -= 360f;
		}
		if (num2 < -180f)
		{
			num2 += 360f;
		}
		currentAngle = num;
		float num3 = num2 / 360f * lengthPerRevolution;
		float num4 = value + direction * num3;
		if (num4 != value)
		{
			OnValueChanged(num4);
		}
	}

	private void OnValueChanged(float newValue)
	{
		float arg = Value;
		value = newValue;
		this.ValueChanged?.Invoke(arg, newValue);
		valueChanged.Invoke(newValue);
	}
}
