using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

public class BodyAnchorProvider : MonoBehaviour
{
    public enum AnchorSource
    {
        None = 0,
        ExternalTransform = 3,
        LeftController = 4,
        RightController = 5
    }

    [Header("Source")]
    [Tooltip("This project uses the left controller as the body anchor.")]
    public AnchorSource source = AnchorSource.LeftController;
    [Tooltip("Rig transform used to convert an anchor into locomotion local space.")]
    public Transform target;
    [Tooltip("Optional world-space waist anchor supplied by another tracker script.")]
    public Transform externalAnchor;

    [Header("Controller Anchor")]
    [Tooltip("XR Origin used to convert raw controller tracking-space poses into locomotion space.")]
    public XROrigin xrOrigin;
    [Tooltip("Optional override for the controller tracking space. Defaults to XROrigin.CameraFloorOffsetObject.")]
    public Transform controllerTrackingSpace;
    public bool requireControllerTrackedPosition = true;
    public bool requireControllerTrackedRotation = true;

    public string LastStatus => lastStatus;

    readonly List<InputDevice> controllerDevices = new List<InputDevice>();
    InputDevice leftControllerDevice;
    InputDevice rightControllerDevice;
    string lastStatus = "Body anchor has not been queried.";

    void Awake()
    {
        source = AnchorSource.LeftController;
    }

    void OnValidate()
    {
        source = AnchorSource.LeftController;
    }

    public bool TryGetAnchorLocalPose(Transform referenceTarget, out Pose localPose)
    {
        switch (source)
        {
            case AnchorSource.ExternalTransform:
                return TryGetExternalAnchor(referenceTarget, out localPose);
            case AnchorSource.LeftController:
                return TryGetControllerAnchor(referenceTarget, true, out localPose);
            case AnchorSource.RightController:
                return TryGetControllerAnchor(referenceTarget, false, out localPose);
            case AnchorSource.None:
            default:
                localPose = default;
                lastStatus = $"Anchor source '{source}' is not supported.";
                return false;
        }
    }

    bool TryGetExternalAnchor(Transform referenceTarget, out Pose localPose)
    {
        localPose = default;

        Transform conversionTarget = referenceTarget != null ? referenceTarget : target;
        if (externalAnchor == null || conversionTarget == null)
        {
            lastStatus = "External body anchor or conversion target is not assigned.";
            return false;
        }

        localPose = new Pose(
            conversionTarget.InverseTransformPoint(externalAnchor.position),
            Quaternion.Inverse(conversionTarget.rotation) * externalAnchor.rotation);
        lastStatus = "External body anchor is active.";
        return true;
    }

    bool TryGetControllerAnchor(Transform referenceTarget, bool leftHand, out Pose localPose)
    {
        localPose = default;

        Transform conversionTarget = referenceTarget != null ? referenceTarget : target;
        Transform trackingSpace = ResolveControllerTrackingSpace(conversionTarget);
        if (conversionTarget == null || trackingSpace == null)
        {
            lastStatus = "Controller body anchor needs a conversion target and tracking space.";
            return false;
        }

        ref InputDevice cachedDevice = ref (leftHand ? ref leftControllerDevice : ref rightControllerDevice);
        if (!cachedDevice.isValid && !TryResolveControllerDevice(leftHand, out cachedDevice))
        {
            return false;
        }

        if (!TryReadControllerPose(cachedDevice, leftHand, out Vector3 trackingPosition, out Quaternion trackingRotation))
        {
            cachedDevice = default;
            return false;
        }

        Vector3 worldPosition = trackingSpace.TransformPoint(trackingPosition);
        Quaternion worldRotation = trackingSpace.rotation * trackingRotation;
        localPose = new Pose(
            conversionTarget.InverseTransformPoint(worldPosition),
            Quaternion.Inverse(conversionTarget.rotation) * worldRotation);
        lastStatus =
            $"{(leftHand ? "Left" : "Right")} controller body anchor is active. " +
            $"Tracking local=({trackingPosition.x:0.000}, {trackingPosition.y:0.000}, {trackingPosition.z:0.000}).";
        return true;
    }

    Transform ResolveControllerTrackingSpace(Transform conversionTarget)
    {
        if (controllerTrackingSpace != null)
        {
            return controllerTrackingSpace;
        }

        if (xrOrigin == null)
        {
            xrOrigin = GetComponent<XROrigin>();
        }

        if (xrOrigin == null && conversionTarget != null)
        {
            xrOrigin = conversionTarget.GetComponent<XROrigin>();
        }

        if (xrOrigin == null)
        {
            xrOrigin = FindAnyObjectByType<XROrigin>();
        }

        if (xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null)
        {
            controllerTrackingSpace = xrOrigin.CameraFloorOffsetObject.transform;
            return controllerTrackingSpace;
        }

        controllerTrackingSpace = conversionTarget;
        return controllerTrackingSpace;
    }

    bool TryResolveControllerDevice(bool leftHand, out InputDevice device)
    {
        controllerDevices.Clear();
        InputDeviceCharacteristics handCharacteristic = leftHand
            ? InputDeviceCharacteristics.Left
            : InputDeviceCharacteristics.Right;
        InputDeviceCharacteristics characteristics =
            InputDeviceCharacteristics.Controller |
            InputDeviceCharacteristics.TrackedDevice |
            handCharacteristic;

        InputDevices.GetDevicesWithCharacteristics(characteristics, controllerDevices);
        for (int i = 0; i < controllerDevices.Count; i++)
        {
            InputDevice candidate = controllerDevices[i];
            if (XRDeviceFilters.IsHandController(candidate, leftHand))
            {
                device = candidate;
                lastStatus = $"Found {(leftHand ? "left" : "right")} controller {XRDeviceFilters.Describe(candidate)}.";
                return true;
            }
        }

        device = default;
        lastStatus = $"{(leftHand ? "Left" : "Right")} controller was not found.";
        return false;
    }

    bool TryReadControllerPose(
        InputDevice device,
        bool leftHand,
        out Vector3 trackingPosition,
        out Quaternion trackingRotation)
    {
        trackingPosition = default;
        trackingRotation = Quaternion.identity;

        bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out trackingPosition);
        bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out trackingRotation);
        bool hasTrackingState = device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState trackingState);
        bool positionTracked = !hasTrackingState || (trackingState & InputTrackingState.Position) != 0;
        bool rotationTracked = !hasTrackingState || (trackingState & InputTrackingState.Rotation) != 0;

        if (!hasPosition || !hasRotation ||
            (requireControllerTrackedPosition && !positionTracked) ||
            (requireControllerTrackedRotation && !rotationTracked))
        {
            lastStatus =
                $"{(leftHand ? "Left" : "Right")} controller body pose is incomplete. " +
                $"Position: {hasPosition}/{positionTracked}, rotation: {hasRotation}/{rotationTracked}.";
            return false;
        }

        return true;
    }
}
