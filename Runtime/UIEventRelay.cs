using UnityEngine;

/// <summary>
/// Bridge added automatically by the tk2d converters to preserve the
/// SendMessage(target, methodName) wiring that tk2dUIItem/tk2dUIToggleButton
/// used, now driven by a UGUI Button.onClick / Toggle.onValueChanged
/// (UnityEvent).
/// </summary>
public class UIEventRelay : MonoBehaviour
{
    public GameObject Target;
    public string MethodName;

    public void Invoke()
    {
        if (Target == null || string.IsNullOrEmpty(MethodName)) return;
        Target.SendMessage(MethodName, SendMessageOptions.DontRequireReceiver);
    }

    public void InvokeBool(bool value)
    {
        if (Target == null || string.IsNullOrEmpty(MethodName)) return;
        Target.SendMessage(MethodName, value, SendMessageOptions.DontRequireReceiver);
    }

    public void InvokeFloat(float value)
    {
        if (Target == null || string.IsNullOrEmpty(MethodName)) return;
        Target.SendMessage(MethodName, value, SendMessageOptions.DontRequireReceiver);
    }
}
