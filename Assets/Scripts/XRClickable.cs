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
