using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NLog;
using UnityEngine;

public class TouchScreenMenuBase : MonoBehaviour
{
	protected class TouchInput
	{
		private Transform fingerTip;

		private bool wasLastAboveScreen;

		private float touchThreshold;

		private float touchReleaseThreshold;

		private float minDistanceToRenderSquared;

		private Vector2 touchStartPosition;

		private bool hasTouchedButton;

		public Vector2 TouchPosition { get; private set; }

		public Vector3 FingerTipPosition { get; private set; }

		public bool BeganTouch { get; private set; }

		public bool IsTouching { get; private set; }

		public Interactor Interactor { get; }

		public bool IsClose { get; private set; }

		public Vector2? Swipe { get; private set; }

		public float Distance { get; private set; }

		public TouchInput(Transform fingerTip, Interactor interactor, float touchThreshold, float touchReleaseThreshold)
		{
			Interactor = interactor;
			this.fingerTip = fingerTip;
			this.touchThreshold = touchThreshold;
			this.touchReleaseThreshold = touchReleaseThreshold;
			minDistanceToRenderSquared = float.MaxValue;
		}

		public TouchInput(Transform fingerTip, Interactor interactor, float touchThreshold, float touchReleaseThreshold, float minDistanceToRenderSquared)
		{
			Interactor = interactor;
			this.fingerTip = fingerTip;
			this.touchThreshold = touchThreshold;
			this.touchReleaseThreshold = touchReleaseThreshold;
			this.minDistanceToRenderSquared = minDistanceToRenderSquared;
		}

		public void UpdateTouch(Vector3 canvasPosition, Matrix4x4 matrix)
		{
			BeganTouch = false;
			Swipe = null;
			FingerTipPosition = fingerTip.position;
			float sqrMagnitude = (canvasPosition - FingerTipPosition).sqrMagnitude;
			Vector3 vector = matrix.MultiplyPoint(FingerTipPosition);
			TouchPosition = vector;
			if (sqrMagnitude > minDistanceToRenderSquared)
			{
				if (IsTouching && !hasTouchedButton)
				{
					Swipe = TouchPosition - touchStartPosition;
				}
				hasTouchedButton = false;
				bool isClose = (IsTouching = false);
				IsClose = isClose;
				return;
			}
			IsClose = true;
			Distance = 0f;
			if (vector.z > 0f)
			{
				bool num = wasLastAboveScreen;
				wasLastAboveScreen = false;
				if (!num)
				{
					return;
				}
			}
			else
			{
				Distance = 0f - vector.z;
				wasLastAboveScreen = true;
			}
			if (Distance > touchThreshold)
			{
				if (Distance > touchReleaseThreshold)
				{
					if (IsTouching && !hasTouchedButton)
					{
						Swipe = TouchPosition - touchStartPosition;
					}
					hasTouchedButton = false;
					IsTouching = false;
				}
			}
			else if (!IsTouching)
			{
				touchStartPosition = TouchPosition;
				bool isClose = (BeganTouch = true);
				IsTouching = isClose;
			}
		}

		public void ButtonTouched()
		{
			hasTouchedButton = true;
		}
	}

	private static NLog.Logger logger = LogManager.GetCurrentClassLogger();

	[SerializeField]
	private GameObject buttonsParent;

	[SerializeField]
	[SquaredValue]
	private float minDistanceToRenderSquared = 0.01f;

	[SerializeField]
	protected Vector2 dimensions;

	[SerializeField]
	private float touchHaptic = 0.2f;

	[SerializeField]
	private float touchThreshold = 0.001f;

	[SerializeField]
	private float touchReleaseThreshold = 0.01f;

	[SerializeField]
	private bool triggerEffectsOnButtonTouchOnly = true;

	[SerializeField]
	private bool alwaysShowButtons;

	[SerializeField]
	private PositionEvent onScreenTouchEffects;

	protected readonly Dictionary<KeyCode, ILabeledTouchScreenButton> instantiatedSuggestionKeys = new Dictionary<KeyCode, ILabeledTouchScreenButton>();

	[SerializeField]
	private PositionEvent onScreenReleasedEffects;

	[SerializeField]
	private bool setupOnStart;

	[SerializeField]
	private bool isMultiTouch = true;

	private ITouchScreenButton activeButtonPress;

	private Rect baseRect;

	private Transform canvasTransform;

	private TouchInput[] touchInputs;

	private Coroutine updateTouch;

	private bool isEnabled;

	private bool isSetupLocal;

	private HashSet<ITouchScreenButton> toRelease = new HashSet<ITouchScreenButton>();

	private HashSet<ITouchScreenButton> touched = new HashSet<ITouchScreenButton>();

	private readonly List<ITouchScreenButton> buttonPressCache = new List<ITouchScreenButton>();

	protected virtual bool IsScreenFree => true;

	protected Transform ButtonsParent => buttonsParent.transform;

	protected Dictionary<ITouchScreenButton, Rect> ButtonMap { get; } = new Dictionary<ITouchScreenButton, Rect>();

	protected float TouchThreshold => touchThreshold;

	protected float TouchReleaseThreshold => touchReleaseThreshold;

	protected virtual Vector2 Offset { get; set; }

	public virtual List<ITouchScreenButton> InstantiatedButtons { get; set; } = new List<ITouchScreenButton>();

	public event Action<Vector2> SwipeExecuted;

	public void Setup(bool isLocal)
	{
		logger.Info("{0} Touch Screen setup, isLocal: {1}", this, isLocal);
		if (isLocal)
		{
			canvasTransform = buttonsParent.transform;
			MapButtons();
			if (PlayerController.Current is IHasFingertips hasFingertips)
			{
				touchInputs = new TouchInput[2];
				FingerTipSettings fingerTips = hasFingertips.FingerTips;
				if (alwaysShowButtons)
				{
					touchInputs[0] = new TouchInput(fingerTips.GetFingerTip(isLeftHand: false), PlayerController.Current.RightController.Interactor, touchThreshold, touchReleaseThreshold);
					touchInputs[1] = new TouchInput(fingerTips.GetFingerTip(isLeftHand: true), PlayerController.Current.LeftController.Interactor, touchThreshold, touchReleaseThreshold);
				}
				else
				{
					touchInputs[0] = new TouchInput(fingerTips.GetFingerTip(isLeftHand: false), PlayerController.Current.RightController.Interactor, touchThreshold, touchReleaseThreshold, minDistanceToRenderSquared);
					touchInputs[1] = new TouchInput(fingerTips.GetFingerTip(isLeftHand: true), PlayerController.Current.LeftController.Interactor, touchThreshold, touchReleaseThreshold, minDistanceToRenderSquared);
				}
				isSetupLocal = true;
				if (base.gameObject.activeInHierarchy)
				{
					updateTouch = StartCoroutine(UpdateTouch());
				}
			}
			SetupForLocal();
		}
		if (!alwaysShowButtons)
		{
			buttonsParent.SetActive(value: false);
		}
	}

	protected virtual void SetupForLocal()
	{
	}

	protected void EnableButtons()
	{
		buttonsParent.SetActive(isEnabled);
	}

	protected void DisableButtons()
	{
		buttonsParent.SetActive(value: false);
	}

	private void OnEnable()
	{
		if (isSetupLocal && updateTouch == null)
		{
			updateTouch = StartCoroutine(UpdateTouch());
		}
	}

	private void OnDisable()
	{
		if (updateTouch != null)
		{
			StopCoroutine(updateTouch);
			updateTouch = null;
		}
	}

	public void MapButtons()
	{
		baseRect = new Rect(-dimensions / 2f, dimensions);
		Matrix4x4 worldToLocalMatrix = base.transform.worldToLocalMatrix;
		Vector2 min = baseRect.min;
		Vector2 max = baseRect.max;
		ButtonMap.Clear();
		if (InstantiatedButtons.Count == 0)
		{
			InstantiatedButtons.AddRange(buttonsParent.GetComponentsInChildren<ITouchScreenButton>(includeInactive: true));
		}
		foreach (ITouchScreenButton instantiatedButton in InstantiatedButtons)
		{
			MapKey(worldToLocalMatrix, instantiatedButton, ref min, ref max);
		}
		foreach (ILabeledTouchScreenButton value in instantiatedSuggestionKeys.Values)
		{
			MapKey(worldToLocalMatrix, value, ref min, ref max);
		}
		baseRect.min = min;
		baseRect.max = max;
	}

	private void MapKey(Matrix4x4 matrix, ITouchScreenButton button, ref Vector2 min, ref Vector2 max)
	{
		Vector2 vector = matrix.MultiplyPoint(button.transform.position);
		Rect value = new Rect(vector - button.ButtonSize / 2f, button.ButtonSize);
		if (min.x > value.min.x)
		{
			min.x = value.min.x;
		}
		if (min.y > value.min.y)
		{
			min.y = value.min.y;
		}
		if (max.x < value.max.x)
		{
			max.x = value.max.x;
		}
		if (max.y < value.max.y)
		{
			max.y = value.max.y;
		}
		ButtonMap[button] = value;
	}

	private IEnumerator UpdateTouch()
	{
		while (true)
		{
			yield return CoroutineYields.EndOfFrame;
			isEnabled = false;
			if (IsScreenFree)
			{
				PreTouchCalculation();
				UpdateButtonPresses();
				UpdateButtonReleases();
				PostTouchCalculation();
			}
			if (!alwaysShowButtons && isEnabled != buttonsParent.activeSelf)
			{
				buttonsParent.SetActive(isEnabled);
			}
		}
	}

	private void UpdateButtonPresses()
	{
		Vector3 position = canvasTransform.position;
		Matrix4x4 worldToLocalMatrix = base.transform.worldToLocalMatrix;
		TouchInput[] array = touchInputs;
		foreach (TouchInput touchInput in array)
		{
			if (!touchInput.Interactor.IsInteracting)
			{
				touchInput.UpdateTouch(position, worldToLocalMatrix);
				ProcessButtonPress(touchInput);
				UpdateTouchWithButtons(touchInput);
				if (touchInput.Swipe.HasValue)
				{
					this.SwipeExecuted?.Invoke(touchInput.Swipe.Value);
				}
				if (touchInput.IsClose)
				{
					isEnabled = true;
				}
			}
		}
	}

	private bool IsFirstPressInBounds(TouchInput touch)
	{
		if (touch.BeganTouch)
		{
			return baseRect.Contains(touch.TouchPosition);
		}
		return false;
	}

	private void ProcessButtonPress(TouchInput touch)
	{
		if (!IsFirstPressInBounds(touch))
		{
			return;
		}
		bool flag = false;
		if (IsScreenFree)
		{
			buttonPressCache.Clear();
			foreach (KeyValuePair<ITouchScreenButton, Rect> item in ButtonMap)
			{
				ITouchScreenButton key = item.Key;
				if (IsButtonValid(item, touch))
				{
					if (!isMultiTouch)
					{
						activeButtonPress = key;
					}
					flag = true;
					buttonPressCache.Add(key);
				}
			}
			foreach (ITouchScreenButton item2 in buttonPressCache)
			{
				item2.Press();
				touch.ButtonTouched();
				touched.Add(item2);
			}
		}
		if (!triggerEffectsOnButtonTouchOnly || flag)
		{
			touch.Interactor.Controller.TriggerHaptic(touchHaptic, 0f);
			onScreenTouchEffects.Invoke(touch.FingerTipPosition);
		}
	}

	private bool IsButtonValid(KeyValuePair<ITouchScreenButton, Rect> buttonPair, TouchInput touch)
	{
		if (buttonPair.Key.IsEnabled && buttonPair.Key.gameObject.activeInHierarchy && ValidateMultiTouch(buttonPair.Key))
		{
			return buttonPair.Value.Contains(touch.TouchPosition);
		}
		return false;
	}

	private bool ValidateMultiTouch(ITouchScreenButton button)
	{
		if (!isMultiTouch && activeButtonPress != null)
		{
			return button == activeButtonPress;
		}
		return true;
	}

	private void UpdateButtonReleases()
	{
		bool flag = false;
		Vector3 argument = default(Vector3);
		toRelease.AddAll(ButtonMap.Keys.Where((ITouchScreenButton button) => !touched.Contains(button) && button.State));
		foreach (ITouchScreenButton item in toRelease)
		{
			bool flag2 = false;
			TouchInput[] array = touchInputs;
			foreach (TouchInput touchInput in array)
			{
				if (touchInput.IsTouching && ButtonMap[item].Contains(touchInput.TouchPosition))
				{
					flag2 = true;
					break;
				}
			}
			if (!flag2)
			{
				activeButtonPress = null;
				item.Release();
				flag = true;
				argument = item.transform.position;
			}
		}
		if (!triggerEffectsOnButtonTouchOnly || flag)
		{
			onScreenReleasedEffects.Invoke(argument);
		}
	}

	protected virtual void PreTouchCalculation()
	{
		toRelease.Clear();
		touched.Clear();
	}

	protected virtual void UpdateTouchWithButtons(TouchInput touch)
	{
	}

	protected virtual void PostTouchCalculation()
	{
	}

	protected virtual void OnDrawGizmos()
	{
		Gizmos.matrix = Matrix4x4.TRS(base.transform.position, base.transform.rotation, Vector3.one);
		Vector3 size = dimensions;
		size.z = touchReleaseThreshold - touchThreshold;
		Gizmos.DrawWireCube(Vector3.back * size.z / 2f, size);
	}
}
