namespace TouhouScalePad.Core;

public interface IKeyboardOutput
{
    void ApplyTransition(DpadButtons previous, DpadButtons current);
    void ReleaseAll(DpadButtons pressed);
}
