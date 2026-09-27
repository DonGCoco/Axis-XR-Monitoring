using System.Collections;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Small project glue layer between Meta Interaction SDK and the existing
/// XRClickable presentation callbacks.
///
/// Ray targeting and pinch selection remain fully owned by Interaction SDK.
/// This component only observes the SDK interactable state and forwards
/// Hover/Select state changes to the app UI.
/// </summary>
public class MetaRayInteractableBridge : MonoBehaviour
{
    private RayInteractable _interactable;
    private XRClickable _clickable;
    private bool _bound;
    private Coroutine _bindRoutine;

    public void Configure(
        RayInteractable interactable,
        XRClickable clickable)
    {
        Unbind();

        _interactable = interactable;
        _clickable = clickable;

        if (_bindRoutine != null)
            StopCoroutine(_bindRoutine);

        _bindRoutine =
            StartCoroutine(BindAfterSdkStart());
    }

    private IEnumerator BindAfterSdkStart()
    {
        // Runtime-created Interaction SDK components finish their own Start()
        // initialization on the next frame. Bind after that lifecycle step.
        yield return null;

        if (_interactable == null ||
            _clickable == null)
        {
            yield break;
        }

        _interactable.WhenStateChanged +=
            HandleStateChanged;

        _bound = true;

        ApplyCurrentState(
            _interactable.State);

        _bindRoutine = null;
    }

    private void HandleStateChanged(
        InteractableStateChangeArgs args)
    {
        ApplyCurrentState(args.NewState);

        if (args.NewState ==
            InteractableState.Select)
        {
            _clickable?.InvokeClick();
        }
    }

    private void ApplyCurrentState(
        InteractableState state)
    {
        if (_clickable == null)
            return;

        bool hovered =
            state == InteractableState.Hover ||
            state == InteractableState.Select;

        _clickable.SetHovered(hovered);
    }

    private void OnDisable()
    {
        Unbind();

        if (_clickable != null)
            _clickable.SetHovered(false);
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void Unbind()
    {
        if (_bound &&
            _interactable != null)
        {
            _interactable.WhenStateChanged -=
                HandleStateChanged;
        }

        _bound = false;
    }
}
