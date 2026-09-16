using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Physics;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonCamera
{
    private readonly PristonCameraSettings _settings;
    private readonly float _fieldOfViewRadians;

    private Vector3 _position;
    private float _yaw;
    private float _pitch;
    private MouseState _lastMouse;
    private bool _hasMouse;
    private float _distance;
    private int _projectionWidth = -1;
    private int _projectionHeight = -1;
    private bool _hasPosition;

    public PristonCamera(Vector3 target, Vector3 forward, PristonCameraSettings settings = null)
    {
        _settings = settings ?? PristonCameraSettings.Default;
        _fieldOfViewRadians = _settings.FieldOfViewDegrees * MathF.PI / 180f;
        Reset(target, forward);
    }

    public Matrix View { get; private set; }
    public Matrix Projection { get; private set; }
    public Vector3 Position => _position;
    public Vector3 FlatForward { get; private set; } = Vector3.Forward;
    public Vector3 Target { get; private set; }

    public void Reset(Vector3 target, Vector3 forward)
    {
        _distance = _settings.DefaultDistance;
        _pitch = _settings.DefaultPitch;
        var normalized = NormalizedForward(forward);
        _yaw = MathF.Atan2(normalized.X, normalized.Z);
        _hasPosition = false;
        _hasMouse = false;
        UpdateFollow(target, 0f);
    }

    public void Update(GameTime gameTime, GraphicsDevice graphicsDevice, Vector3 target, Vector3 forward)
    {
        Update(gameTime, graphicsDevice, target, forward, null);
    }

    public void SnapFollow(Vector3 target, IPristonCollisionQuery collisionWorld = null)
    {
        _hasPosition = false;
        UpdateFollow(target, 0f, collisionWorld);
    }

    public void SetScriptedView(GraphicsDevice graphicsDevice, Vector3 position, Vector3 lookAt)
        => SetScriptedView(graphicsDevice, position, lookAt, Vector3.Up);

    public void SetScriptedView(GraphicsDevice graphicsDevice, Vector3 position, Vector3 lookAt, Vector3 up)
    {
        EnsureProjection(graphicsDevice);
        _position = position;
        _hasPosition = true;
        Target = lookAt;

        var horizontal = new Vector3(lookAt.X - position.X, 0f, lookAt.Z - position.Z);
        FlatForward = horizontal.LengthSquared() > 0.0001f ? Vector3.Normalize(horizontal) : Vector3.Forward;
        View = Matrix.CreateLookAt(position, lookAt, up);
    }

    public void Update(GameTime gameTime, GraphicsDevice graphicsDevice, Vector3 target, Vector3 forward,
        IPristonCollisionQuery collisionWorld, bool pointerCaptured = false, bool inputEnabled = true)
    {
        UpdateOrbit(Mouse.GetState(), pointerCaptured, graphicsDevice.Viewport.Bounds, (float)gameTime.ElapsedGameTime.TotalSeconds, inputEnabled);
        EnsureProjection(graphicsDevice);
        UpdateFollow(target, (float)gameTime.ElapsedGameTime.TotalSeconds, collisionWorld);
    }

    public void UpdateOrbit(MouseState mouse, bool pointerCaptured = false, Rectangle viewport = default, float deltaSeconds = 0f, bool inputEnabled = true)
    {
        if (!inputEnabled)
        {
            _hasMouse = false;
            return;
        }

        if (!_hasMouse)
        {
            _lastMouse = mouse;
            _hasMouse = true;
            return;
        }

        if (!pointerCaptured && mouse.MiddleButton == ButtonState.Pressed && _lastMouse.MiddleButton == ButtonState.Pressed)
        {
            var dx = mouse.X - _lastMouse.X;
            var dy = mouse.Y - _lastMouse.Y;
            _yaw -= dx * _settings.MouseOrbitSensitivity;
            _pitch = MathHelper.Clamp(_pitch + dy * _settings.MouseOrbitSensitivity, _settings.MinPitch, _settings.MaxPitch);
        }

        if (!pointerCaptured && mouse.MiddleButton == ButtonState.Released && viewport.Contains(mouse.Position)
            && viewport.Width > _settings.EdgeOrbitWidth * 2 && float.IsFinite(deltaSeconds) && deltaSeconds > 0f)
        {
            if (mouse.X < viewport.Left + _settings.EdgeOrbitWidth)
            {
                _yaw -= _settings.EdgeOrbitRadiansPerSecond * deltaSeconds;
            }
            else if (mouse.X > viewport.Right - _settings.EdgeOrbitWidth)
            {
                _yaw += _settings.EdgeOrbitRadiansPerSecond * deltaSeconds;
            }
        }

        var scroll = mouse.ScrollWheelValue - _lastMouse.ScrollWheelValue;
        if (!pointerCaptured && scroll != 0)
        {
            _distance = MathHelper.Clamp(_distance - scroll * _settings.MouseZoomSensitivity, _settings.MinDistance, _settings.MaxDistance);
        }

        _lastMouse = mouse;
    }

    private void EnsureProjection(GraphicsDevice graphicsDevice)
    {
        var width = Math.Max(1, graphicsDevice.Viewport.Width);
        var height = Math.Max(1, graphicsDevice.Viewport.Height);
        if (width == _projectionWidth && height == _projectionHeight)
        {
            return;
        }

        _projectionWidth = width;
        _projectionHeight = height;
        Projection = Matrix.CreatePerspectiveFieldOfView(
            _fieldOfViewRadians, width / (float)height, _settings.NearPlane, _settings.FarPlane);
    }

    public void UpdateFollow(Vector3 target, float deltaSeconds, IPristonCollisionQuery collisionWorld = null)
    {
        var lookAt = target + new Vector3(0f, _settings.TargetHeight, 0f);
        var alpha = 1f;
        if (_hasPosition && float.IsFinite(deltaSeconds) && deltaSeconds > 0f)
        {
            alpha = 1f - MathF.Exp(-_settings.FollowSmoothing * deltaSeconds);
            lookAt = Vector3.Lerp(Target, lookAt, alpha);
        }

        var desired = lookAt - CameraForward() * _distance;
        if (_hasPosition)
        {
            var translated = _position + lookAt - Target;
            desired = Vector3.Lerp(translated, desired, alpha);
        }

        Target = lookAt;
        var previous = _hasPosition ? _position : desired;
        _position = PristonCameraCollision.Resolve(previous, desired, lookAt.Y, _settings, collisionWorld);
        _hasPosition = true;

        FlatForward = new Vector3(MathF.Sin(_yaw), 0f, MathF.Cos(_yaw));
        var viewTarget = Vector3.DistanceSquared(_position, lookAt) > 0.000001f ? lookAt : _position + CameraForward();
        View = Matrix.CreateLookAt(_position, viewTarget, Vector3.Up);
    }

    private static Vector3 NormalizedForward(Vector3 forward)
    {
        if (forward.LengthSquared() < 0.0001f)
        {
            return Vector3.Forward;
        }

        forward.Y = 0f;
        if (forward.LengthSquared() < 0.0001f)
        {
            return Vector3.Forward;
        }

        forward.Normalize();
        return forward;
    }

    private Vector3 CameraForward()
    {
        var horizontal = MathF.Cos(_pitch);
        return new Vector3(
            MathF.Sin(_yaw) * horizontal,
            -MathF.Sin(_pitch),
            MathF.Cos(_yaw) * horizontal);
    }
}
