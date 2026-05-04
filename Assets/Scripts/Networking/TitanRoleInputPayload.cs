using System;
using Unity.Netcode;
using UnityEngine;

public struct TitanRoleInputPayload : INetworkSerializable, IEquatable<TitanRoleInputPayload>
{
    public float MouseX;
    public float MouseY;
    public float MouseDeltaX;
    public float MouseDeltaY;
    public bool RightMouseHeld;
    public bool RightMousePressedThisFrame;
    public bool RightMouseDetachBuffered;

    public float BodyForward;
    public float BodyStrafe;
    public float BodyTurn;
    public float BodyWaist;

    public float LeftArmElbow;
    public float RightArmElbow;
    public float LeftLegKnee;
    public float RightLegKnee;
    public float LeftLegAnkle;
    public float RightLegAnkle;

    public TitanRoleInputPayload(in TitanAggregatedInput input)
    {
        MouseX = input.MousePosition.x;
        MouseY = input.MousePosition.y;
        MouseDeltaX = input.MouseDelta.x;
        MouseDeltaY = input.MouseDelta.y;
        RightMouseHeld = input.RightMouseHeld;
        RightMousePressedThisFrame = input.RightMousePressedThisFrame;
        RightMouseDetachBuffered = input.RightMouseDetachBuffered;
        BodyForward = input.BodyForward;
        BodyStrafe = input.BodyStrafe;
        BodyTurn = input.BodyTurn;
        BodyWaist = input.BodyWaist;
        LeftArmElbow = input.LeftArmElbow;
        RightArmElbow = input.RightArmElbow;
        LeftLegKnee = input.LeftLegKnee;
        RightLegKnee = input.RightLegKnee;
        LeftLegAnkle = input.LeftLegAnkle;
        RightLegAnkle = input.RightLegAnkle;
    }

    public TitanAggregatedInput ToAggregatedInput()
    {
        return new TitanAggregatedInput
        {
            MousePosition = new Vector2(MouseX, MouseY),
            MouseDelta = new Vector2(MouseDeltaX, MouseDeltaY),
            RightMouseHeld = RightMouseHeld,
            RightMousePressedThisFrame = RightMousePressedThisFrame,
            RightMouseDetachBuffered = RightMouseDetachBuffered,
            BodyForward = BodyForward,
            BodyStrafe = BodyStrafe,
            BodyTurn = BodyTurn,
            BodyWaist = BodyWaist,
            LeftArmElbow = LeftArmElbow,
            RightArmElbow = RightArmElbow,
            LeftLegKnee = LeftLegKnee,
            RightLegKnee = RightLegKnee,
            LeftLegAnkle = LeftLegAnkle,
            RightLegAnkle = RightLegAnkle,
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref MouseX);
        serializer.SerializeValue(ref MouseY);
        serializer.SerializeValue(ref MouseDeltaX);
        serializer.SerializeValue(ref MouseDeltaY);
        serializer.SerializeValue(ref RightMouseHeld);
        serializer.SerializeValue(ref RightMousePressedThisFrame);
        serializer.SerializeValue(ref RightMouseDetachBuffered);
        serializer.SerializeValue(ref BodyForward);
        serializer.SerializeValue(ref BodyStrafe);
        serializer.SerializeValue(ref BodyTurn);
        serializer.SerializeValue(ref BodyWaist);
        serializer.SerializeValue(ref LeftArmElbow);
        serializer.SerializeValue(ref RightArmElbow);
        serializer.SerializeValue(ref LeftLegKnee);
        serializer.SerializeValue(ref RightLegKnee);
        serializer.SerializeValue(ref LeftLegAnkle);
        serializer.SerializeValue(ref RightLegAnkle);
    }

    public bool Equals(TitanRoleInputPayload other)
    {
        return MouseX.Equals(other.MouseX)
            && MouseY.Equals(other.MouseY)
            && MouseDeltaX.Equals(other.MouseDeltaX)
            && MouseDeltaY.Equals(other.MouseDeltaY)
            && RightMouseHeld == other.RightMouseHeld
            && RightMousePressedThisFrame == other.RightMousePressedThisFrame
            && RightMouseDetachBuffered == other.RightMouseDetachBuffered
            && BodyForward.Equals(other.BodyForward)
            && BodyStrafe.Equals(other.BodyStrafe)
            && BodyTurn.Equals(other.BodyTurn)
            && BodyWaist.Equals(other.BodyWaist)
            && LeftArmElbow.Equals(other.LeftArmElbow)
            && RightArmElbow.Equals(other.RightArmElbow)
            && LeftLegKnee.Equals(other.LeftLegKnee)
            && RightLegKnee.Equals(other.RightLegKnee)
            && LeftLegAnkle.Equals(other.LeftLegAnkle)
            && RightLegAnkle.Equals(other.RightLegAnkle);
    }
}

public struct TitanRigPosePayload : INetworkSerializable, IEquatable<TitanRigPosePayload>
{
    public bool IsValid;

    public Vector3 RootPosition;
    public Quaternion RootRotation;

    public bool HasLeftShoulder;
    public Quaternion LeftShoulderRotation;
    public bool HasLeftElbow;
    public Quaternion LeftElbowRotation;

    public bool HasRightShoulder;
    public Quaternion RightShoulderRotation;
    public bool HasRightElbow;
    public Quaternion RightElbowRotation;

    public bool HasLeftHip;
    public Quaternion LeftHipRotation;
    public bool HasLeftKnee;
    public Quaternion LeftKneeRotation;
    public bool HasLeftFoot;
    public Quaternion LeftFootRotation;

    public bool HasRightHip;
    public Quaternion RightHipRotation;
    public bool HasRightKnee;
    public Quaternion RightKneeRotation;
    public bool HasRightFoot;
    public Quaternion RightFootRotation;

    public bool HasSpine;
    public Quaternion SpineRotation;

    public TitanRigPosePayload(in TitanRigPoseSnapshot snapshot)
    {
        IsValid = true;

        RootPosition = snapshot.RootPosition;
        RootRotation = snapshot.RootRotation;

        HasLeftShoulder = snapshot.HasLeftShoulder;
        LeftShoulderRotation = snapshot.LeftShoulderRotation;
        HasLeftElbow = snapshot.HasLeftElbow;
        LeftElbowRotation = snapshot.LeftElbowRotation;

        HasRightShoulder = snapshot.HasRightShoulder;
        RightShoulderRotation = snapshot.RightShoulderRotation;
        HasRightElbow = snapshot.HasRightElbow;
        RightElbowRotation = snapshot.RightElbowRotation;

        HasLeftHip = snapshot.HasLeftHip;
        LeftHipRotation = snapshot.LeftHipRotation;
        HasLeftKnee = snapshot.HasLeftKnee;
        LeftKneeRotation = snapshot.LeftKneeRotation;
        HasLeftFoot = snapshot.HasLeftFoot;
        LeftFootRotation = snapshot.LeftFootRotation;

        HasRightHip = snapshot.HasRightHip;
        RightHipRotation = snapshot.RightHipRotation;
        HasRightKnee = snapshot.HasRightKnee;
        RightKneeRotation = snapshot.RightKneeRotation;
        HasRightFoot = snapshot.HasRightFoot;
        RightFootRotation = snapshot.RightFootRotation;

        HasSpine = snapshot.HasSpine;
        SpineRotation = snapshot.SpineRotation;
    }

    public TitanRigPoseSnapshot ToSnapshot()
    {
        return new TitanRigPoseSnapshot
        {
            RootPosition = RootPosition,
            RootRotation = RootRotation,

            HasLeftShoulder = HasLeftShoulder,
            LeftShoulderRotation = LeftShoulderRotation,
            HasLeftElbow = HasLeftElbow,
            LeftElbowRotation = LeftElbowRotation,

            HasRightShoulder = HasRightShoulder,
            RightShoulderRotation = RightShoulderRotation,
            HasRightElbow = HasRightElbow,
            RightElbowRotation = RightElbowRotation,

            HasLeftHip = HasLeftHip,
            LeftHipRotation = LeftHipRotation,
            HasLeftKnee = HasLeftKnee,
            LeftKneeRotation = LeftKneeRotation,
            HasLeftFoot = HasLeftFoot,
            LeftFootRotation = LeftFootRotation,

            HasRightHip = HasRightHip,
            RightHipRotation = RightHipRotation,
            HasRightKnee = HasRightKnee,
            RightKneeRotation = RightKneeRotation,
            HasRightFoot = HasRightFoot,
            RightFootRotation = RightFootRotation,

            HasSpine = HasSpine,
            SpineRotation = SpineRotation,
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref IsValid);

        serializer.SerializeValue(ref RootPosition);
        serializer.SerializeValue(ref RootRotation);

        serializer.SerializeValue(ref HasLeftShoulder);
        serializer.SerializeValue(ref LeftShoulderRotation);
        serializer.SerializeValue(ref HasLeftElbow);
        serializer.SerializeValue(ref LeftElbowRotation);

        serializer.SerializeValue(ref HasRightShoulder);
        serializer.SerializeValue(ref RightShoulderRotation);
        serializer.SerializeValue(ref HasRightElbow);
        serializer.SerializeValue(ref RightElbowRotation);

        serializer.SerializeValue(ref HasLeftHip);
        serializer.SerializeValue(ref LeftHipRotation);
        serializer.SerializeValue(ref HasLeftKnee);
        serializer.SerializeValue(ref LeftKneeRotation);
        serializer.SerializeValue(ref HasLeftFoot);
        serializer.SerializeValue(ref LeftFootRotation);

        serializer.SerializeValue(ref HasRightHip);
        serializer.SerializeValue(ref RightHipRotation);
        serializer.SerializeValue(ref HasRightKnee);
        serializer.SerializeValue(ref RightKneeRotation);
        serializer.SerializeValue(ref HasRightFoot);
        serializer.SerializeValue(ref RightFootRotation);

        serializer.SerializeValue(ref HasSpine);
        serializer.SerializeValue(ref SpineRotation);
    }

    public bool Equals(TitanRigPosePayload other)
    {
        return IsValid == other.IsValid
            && RootPosition.Equals(other.RootPosition)
            && RootRotation.Equals(other.RootRotation)
            && HasLeftShoulder == other.HasLeftShoulder
            && LeftShoulderRotation.Equals(other.LeftShoulderRotation)
            && HasLeftElbow == other.HasLeftElbow
            && LeftElbowRotation.Equals(other.LeftElbowRotation)
            && HasRightShoulder == other.HasRightShoulder
            && RightShoulderRotation.Equals(other.RightShoulderRotation)
            && HasRightElbow == other.HasRightElbow
            && RightElbowRotation.Equals(other.RightElbowRotation)
            && HasLeftHip == other.HasLeftHip
            && LeftHipRotation.Equals(other.LeftHipRotation)
            && HasLeftKnee == other.HasLeftKnee
            && LeftKneeRotation.Equals(other.LeftKneeRotation)
            && HasLeftFoot == other.HasLeftFoot
            && LeftFootRotation.Equals(other.LeftFootRotation)
            && HasRightHip == other.HasRightHip
            && RightHipRotation.Equals(other.RightHipRotation)
            && HasRightKnee == other.HasRightKnee
            && RightKneeRotation.Equals(other.RightKneeRotation)
            && HasRightFoot == other.HasRightFoot
            && RightFootRotation.Equals(other.RightFootRotation)
            && HasSpine == other.HasSpine
            && SpineRotation.Equals(other.SpineRotation);
    }
}
