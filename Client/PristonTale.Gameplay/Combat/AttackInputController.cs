namespace PristonTale.Gameplay.Combat;

public readonly record struct AttackRequest(uint TargetId, bool Sustained, ulong Sequence);

public sealed class AttackInputController
{
    private bool _automatic;
    private bool _rightButton;
    private bool _holding;
    private bool _acceptState;

    public uint TargetId { get; private set; }
    public ulong Sequence { get; private set; }

    public AttackRequest Press(uint targetId, bool automatic, bool rightButton)
    {
        TargetId = targetId;
        Sequence++;
        _automatic = automatic;
        _rightButton = rightButton;
        _holding = true;
        _acceptState = true;
        return new AttackRequest(targetId, true, Sequence);
    }

    public AttackRequest? Release(bool leftHeld, bool rightHeld)
    {
        if (!_holding || (_rightButton ? rightHeld : leftHeld))
        {
            return null;
        }

        _holding = false;
        if (_automatic || TargetId == 0)
        {
            return null;
        }

        return new AttackRequest(TargetId, false, Sequence);
    }

    public void Reconcile(ulong sequence, uint targetId)
    {
        if (!_acceptState || sequence < Sequence)
        {
            return;
        }

        TargetId = targetId;
        if (targetId == 0)
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        TargetId = 0;
        _holding = false;
        _acceptState = false;
    }
}
