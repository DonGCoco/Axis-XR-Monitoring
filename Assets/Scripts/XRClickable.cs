using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class XRClickable : MonoBehaviour
{
    private Image _targetImage;
    private Action _onClick;
    private Color _normalColor;
    private Color _hoverColor;
    private Color _pressedColor;
    private bool _hovered;
    private bool _pressed;
    private Vector3 _normalScale;
    private Coroutine _pressRoutine;
    private float _lastInvokeTime = -10f;
    private float _hoverStartTime = -1f;

    // The Interaction SDK ray provides hover, but on this rig its Select event
    // is not delivered for hand or controller input. Hover is therefore used as
    // the aiming signal and a click is accepted from any of three sources:
    // an OVRHand pinch, a controller button, or a short dwell on the button.
    // The dwell keeps the prototype usable even when no button event arrives.
    private const float DwellSeconds = 1.1f;

    private static OVRHand[] _trackedHands;
    private static bool[] _pinchState;
    private static bool _handSearchDone;
    private static int _pinchFrame = -1;
    private static bool _pinchEdge;
    private static float _lastAnyClickTime = -10f;

    private void Update()
    {
        if (!_hovered)
        {
            _hoverStartTime = -1f;
            return;
        }

        if (_hoverStartTime < 0f)
            _hoverStartTime = Time.unscaledTime;

        if (Time.unscaledTime - _lastInvokeTime < 0.35f)
            return;

        if (PinchStartedThisFrame() || ControllerSelectPressed())
        {
            InvokeClick();
            _hoverStartTime = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - _hoverStartTime >= DwellSeconds &&
            Time.unscaledTime - _lastAnyClickTime >= 0.9f)
        {
            InvokeClick();
            _hoverStartTime = Time.unscaledTime;
        }
    }

    private static bool ControllerSelectPressed()
    {
        return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)
            || OVRInput.GetDown(OVRInput.Button.One)
            || OVRInput.GetDown(OVRInput.Button.Two);
    }

    private static bool PinchStartedThisFrame()
    {
        // Every button runs Update, so resolve the shared pinch edge once per
        // frame instead of once per button.
        if (_pinchFrame == Time.frameCount)
            return _pinchEdge;

        _pinchFrame = Time.frameCount;
        _pinchEdge = false;

        // Scene search is expensive. Do it once.
        if (!_handSearchDone)
        {
            _handSearchDone = true;

            _trackedHands = FindObjectsByType<OVRHand>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (_trackedHands == null || _trackedHands.Length == 0)
            {
                _trackedHands = null;
                Debug.Log("XRClickable: no OVRHand in scene, relying on dwell and controller input.");
                return false;
            }

            _pinchState = new bool[_trackedHands.Length];
        }

        if (_trackedHands == null)
            return false;

        for (int i = 0; i < _trackedHands.Length; i++)
        {
            OVRHand hand = _trackedHands[i];
            if (hand == null)
                continue;

            bool pinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);

            if (pinching && !_pinchState[i])
                _pinchEdge = true;

            _pinchState[i] = pinching;
        }

        return _pinchEdge;
    }

    public void Initialize(
        Image targetImage,
        Action onClick,
        Color normalColor,
        Color hoverColor)
    {
        _targetImage = targetImage;
        _onClick = onClick;
        _normalColor = normalColor;
        _hoverColor = hoverColor;
        _pressedColor = Color.Lerp(hoverColor, Color.white, 0.18f);
        _normalScale = transform.localScale;
        ApplyColor();
    }

    public void SetHovered(bool hovered)
    {
        if (_hovered == hovered)
            return;

        _hovered = hovered;
        ApplyColor();
    }

    public void InvokeClick()
    {
        // A single pinch can reach this method from both the SDK Select event
        // and the fallbacks in the same frame. Ignore the duplicate.
        if (Time.unscaledTime - _lastInvokeTime < 0.35f)
            return;

        _lastInvokeTime = Time.unscaledTime;
        _lastAnyClickTime = Time.unscaledTime;

        if (_pressRoutine != null)
            StopCoroutine(_pressRoutine);

        _pressRoutine = StartCoroutine(PressFeedback());
        _onClick?.Invoke();
    }

    private IEnumerator PressFeedback()
    {
        _pressed = true;
        transform.localScale = _normalScale * 0.97f;
        ApplyColor();

        yield return new WaitForSecondsRealtime(0.09f);

        _pressed = false;
        transform.localScale = _normalScale;
        ApplyColor();
        _pressRoutine = null;
    }

    private void OnDisable()
    {
        transform.localScale = _normalScale;
        _pressed = false;
    }

    private void ApplyColor()
    {
        if (_targetImage == null)
            return;

        if (_pressed)
            _targetImage.color = _pressedColor;
        else
            _targetImage.color = _hovered ? _hoverColor : _normalColor;
    }
}
