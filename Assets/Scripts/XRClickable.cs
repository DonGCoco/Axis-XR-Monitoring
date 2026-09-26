using System;
using UnityEngine;
using UnityEngine.UI;

public class XRClickable : MonoBehaviour
{
    private Image _targetImage;
    private Action _onClick;
    private Color _normalColor;
    private Color _hoverColor;
    private bool _hovered;

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
        _onClick?.Invoke();
    }

    private void ApplyColor()
    {
        if (_targetImage != null)
            _targetImage.color = _hovered ? _hoverColor : _normalColor;
    }
}
