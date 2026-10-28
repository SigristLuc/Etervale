using System.Threading.Channels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;
using NumVector3 = System.Numerics.Vector3;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Engine3D;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.World;

// The local player's motion: the server's state and events for it, and the clips its body and head play.
public sealed partial class GameScene
{
    private void SetPlayerMotion(MotionCode motion)
    {
        if (_renderer == null || !_playerBodyActor.IsValid)
            return;
        PlayerActors.SetMotion(_actorScene, motion);
        PlayerActors.SetPlaybackRate(_actorScene, PlayerRateFor(motion));
    }

    private void RestartPlayerMotion(MotionCode motion)
    {
        if (_renderer == null || !_playerBodyActor.IsValid)
            return;
        PlayerActors.RestartMotion(_actorScene, motion);
        PlayerActors.SetPlaybackRate(_actorScene, PlayerRateFor(motion));
    }

    private void ApplyPlayerCommand(MotionCommand command)
    {
        if (command.Kind == MotionCommandKind.None)
            return;
        if (command.Kind == MotionCommandKind.Restart)
            RestartPlayerMotion(command.Motion);
        else
            SetPlayerMotion(command.Motion);
        if (_player is not null && _playerBody is not null)
            PlayPlayerMotionSound(_player.Class.Class, command.Motion, _playerBody.Position);
    }

    public void DebugForceMotion(MotionCode motion)
        => ApplyPlayerCommand(_playerController.ApplyAuthoritative(motion));

    private void OnSelfSpawn(EntitySpawnInfo spawn)
    {
        _selfMaxLife = spawn.MaxLife;
        OnSelfState(new EntityStateInfo(spawn.EntityId, spawn.Position, spawn.Facing, spawn.MoveState,
            spawn.Life, spawn.MoveSpeed, spawn.AttackRate, spawn.AttackTargetId, spawn.AttackSequence));
    }

    private void OnSelfState(EntityStateInfo state)
    {
        _attackInput.Reconcile(state.AttackSequence, state.AttackTargetId);
        _selfAttacking = state.MoveState == MotionState.Attack;
        int previousLife = _selfLife;
        _selfLife = state.Life;
        _serverMoveSpeed = state.MoveSpeed;
        _serverAttackRate = state.AttackRate;
        // The client owns locomotion; server echoes lag behind it, so only FieldWarp moves the local avatar.
        _serverPlanarPosition = new Vector2(state.Position.X, state.Position.Z);
        _serverCollisionHeight = state.Position.Y;
        var wire = MotionCodes.Of(state.MoveState);
        bool wasDead = _playerController.IsDead;
        switch (wire)
        {
            case MotionCode.Eat:
            case MotionCode.Yahoo:
                ApplyPlayerCommand(_playerController.ApplyAuthoritative(wire));
                break;

            case MotionCode.Attack:
                ApplyPlayerCommand(_playerController.ApplyAuthoritative(MotionCode.Attack));
                break;

            case MotionCode.Skill:
                // The skill presentation plays the cast's own clip from the server's skill event.
                break;

            case MotionCode.Damage:
                {
                    var command = _playerController.ApplyAuthoritative(MotionCode.Damage);
                    if (command.Kind == MotionCommandKind.None && state.Life < previousLife)
                    {
                        command = _playerController.Retrigger(MotionCode.Damage);
                    }

                    ApplyPlayerCommand(command);
                    break;
                }

            case MotionCode.Dead:
                if (!wasDead)
                {
                    ApplyPlayerCommand(_playerController.ApplyAuthoritative(MotionCode.Dead));
                    ClearAttackTarget();
                    _hasMoveTarget = false;
                }

                break;
            default:
                if (wasDead)
                {
                    RespawnAt(state.Position);
                }
                else if (_playerController.Current is MotionCode.Damage or MotionCode.Attack or MotionCode.Eat
                         or MotionCode.Yahoo)
                {
                    ApplyPlayerCommand(_playerController.ApplyAuthoritative(MotionCode.Stand));
                }

                break;
        }
    }

    private void OnSelfCombatEvent(CombatEventInfo combatEvent)
    {
        if (combatEvent.Kind == CombatEventKind.PotionBegin)
        {
            ApplyPlayerCommand(_playerController.Retrigger(MotionCode.Eat));
        }
        else if (combatEvent.Kind == CombatEventKind.SwingBegin && !_playerController.LocomotionFrozen)
        {
            _selfAttacking = true;
            ApplyPlayerCommand(_playerController.Retrigger(MotionCode.Attack));
        }
        else if (combatEvent.Kind == CombatEventKind.Hit && !combatEvent.Killed && _playerController.Current == MotionCode.Damage)
        {
            ApplyPlayerCommand(_playerController.Retrigger(MotionCode.Damage));
        }
    }

    private float PlayerRateFor(MotionCode motion)
    {
        if (_player == null)
        {
            return 1f;
        }

        var d = _player.Derived;
        return motion switch
        {
            MotionCode.Walk or MotionCode.Run => Locomotion.StepRate(_serverMoveSpeed ?? d.MoveSpeed, motion == MotionCode.Run),
            MotionCode.Attack or MotionCode.Skill => _serverMoveSpeed.HasValue ? _serverAttackRate : AnimationRate.AttackRate(d.AttackSpeed),
            _ => 1f,
        };
    }
}
