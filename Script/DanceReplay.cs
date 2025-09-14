using System;
using JLChnToZ.VRC.VVMW;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;


[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DanceReplay : UdonSharpBehaviour
{
    // sourcePlayer is also the owner
    private VRCPlayerApi sourcePlayer;
    public int maxRecordingFrames;

    [HideInInspector, UdonSynced]
    public String sourcePlayerName;

    // Recorded properties
    // All local
    private Vector3[][] sourceTs;
    private Quaternion[][] sourceQs;

    private Vector3[] sourceT_Root;
    private Quaternion[] sourceQ_Root;

    private float[] timeStamps;

    // This frame properties
    private Vector3[] lerpedSourceTs;
    private Quaternion[] lerpedSourceQs;

    private Vector3 lerpedSourceT_Root;
    private Quaternion lerpedSourceQ_Root;

    // Current Calibration results, maybe from other players
    // 4 * 4 * 55 = 880Bytes, synced
    [UdonSynced] private Quaternion[] currentSourcePostQs;
    [UdonSynced] private float currentSourceScale;

    // Local player calibration results
    private Quaternion[] localSourcePostQs;
    private float localSourceScale;

    // Calibration cache
    private Vector3[] calibrateTs;
    private Quaternion[] calibrateQs;
    private Vector3 calibrateT_Root;
    private Quaternion calibrateQ_Root;


    // Record root
    public Transform sourceRoot;

    // the target is the puppet
    public Animator targetAnimator;
    private Transform[] targetBones;
    private Transform targetRoot;
    private Quaternion[] targetPostQs, targetRestLQs;
    private Vector3 targetUnitScale;

    // These are indicators, not important, we don't use them now
    // the handles are attached to source bones (for example, as indicators)
    // public  Transform    handleRoot;
    // public  GameObject   handlePrefab;
    // private Transform[]  handles;

    private int currentFrame = 0;
    private int length = 0;

    [HideInInspector] public bool recording = false;
    [HideInInspector] public bool replaying = false;
    [HideInInspector] public bool calibrated = false;
    private bool justReplayed = false;

    private bool lastTickPaused = false;
    private String lastAvailableURL = "";

    // Video Player related
    // Vizvid
    public Core core;


    private const float MinJumpInterval = 0.5f;

    private void Start()
    {
        // Recording frames for source
        sourceTs = new Vector3[maxRecordingFrames][];
        sourceQs = new Quaternion[maxRecordingFrames][];


        // Source properties but not recorded
        sourceT_Root = new Vector3[maxRecordingFrames];
        sourceQ_Root = new Quaternion[maxRecordingFrames];
        currentSourcePostQs = new Quaternion[BoneCount];
        localSourcePostQs = new Quaternion[BoneCount];

        // Target related
        targetPostQs = new Quaternion[BoneCount];
        targetRestLQs = new Quaternion[BoneCount];

        timeStamps = new float[maxRecordingFrames];

        // Lerp var
        lerpedSourceTs = new Vector3[BoneCount];
        lerpedSourceQs = new Quaternion[BoneCount];
        lerpedSourceQ_Root = new Quaternion();
        lerpedSourceT_Root = new Vector3();

        // calibrate cache
        calibrateTs = new Vector3[BoneCount];
        calibrateQs = new Quaternion[BoneCount];
        calibrateT_Root = new Vector3();
        calibrateQ_Root = new Quaternion();

        InitializeTarget();
    }

    void Record()
    {
        timeStamps[currentFrame] = core.Time;
        sourceT_Root[currentFrame] = sourcePlayer.GetPosition();
        sourceQ_Root[currentFrame] = sourcePlayer.GetRotation();
        sourceTs[currentFrame] = new Vector3[BoneCount];
        sourceQs[currentFrame] = new Quaternion[BoneCount];
        for (int i = 0; i < BoneCount; i++)
        {
            sourceTs[currentFrame][i] = sourcePlayer.GetBonePosition((HumanBodyBones)i);
            sourceQs[currentFrame][i] = sourcePlayer.GetBoneRotation((HumanBodyBones)i);
        }
    }

    [NetworkCallable]
    public void StartRecord()
    {
        if (NetworkCalling.InNetworkCall)
        {
            Debug.Log(NetworkCalling.CallingPlayer != Networking.LocalPlayer
                ? "Received network event StartRecording"
                : "Sent network event StartRecording");
        }


        if (recording)
        {
            return;
        }

        StopReplay();
        sourcePlayer = NetworkCalling.CallingPlayer;

        if (sourcePlayer == Networking.LocalPlayer)
        {
            // It's the local player that is recording so sync our calibration data
            Debug.Log("Sending our calibration results...");
            Networking.SetOwner(sourcePlayer, gameObject);
            for (int i = 0; i < BoneCount; i++)
            {
                currentSourcePostQs[i] = localSourcePostQs[i];
            }

            currentSourceScale = localSourceScale;
            sourcePlayerName = sourcePlayer.displayName;
            RequestSerialization();
        }


        recording = true;
        currentFrame = 0;
        Debug.Log("Starting recording: currentFrame:" + currentFrame + ", currentLength:" + length + ", local:" +
                  (NetworkCalling.CallingPlayer == Networking.LocalPlayer));
        if (core.IsPaused && Networking.GetOwner(gameObject) == Networking.LocalPlayer)
        {
            Debug.Log("We are the owner so Play the player");
            core.Play();
        }
    }

    [NetworkCallable]
    public void StopRecord()
    {
        if (NetworkCalling.InNetworkCall)
        {
            Debug.Log(NetworkCalling.CallingPlayer != Networking.LocalPlayer
                ? "Received network event StopRecording"
                : "Sent network event StopRecording");
        }

        if (!recording)
        {
            return;
        }

        recording = false;
        length = currentFrame;

        Debug.Log("Stopping recording: currentFrame:" + currentFrame + ", currentLength:" + length + ", local:" +
                  (NetworkCalling.CallingPlayer == Networking.LocalPlayer));

        timeStamps[length] = timeStamps[length - 1];
        sourceT_Root[length] = sourceT_Root[length - 1];
        sourceQ_Root[length] = sourceQ_Root[length - 1];

        sourceQs[length] = new Quaternion[BoneCount];
        sourceTs[length] = new Vector3[BoneCount];
        for (int i = 0; i < BoneCount; i++)
        {
            sourceQs[length][i] = sourceQs[length - 1][i];
            sourceTs[length][i] = sourceTs[length - 1][i];
        }

        if (Networking.GetOwner(gameObject) == Networking.LocalPlayer)
        {
            Debug.Log("We are the owner so Pause the player.");
            core.Pause();
        }
    }

    [NetworkCallable]
    public void StartReplay()
    {
        if (NetworkCalling.InNetworkCall)
        {
            Debug.Log(NetworkCalling.CallingPlayer != Networking.LocalPlayer
                ? "Received network event StartReplaying"
                : "Sent network event StartReplaying");
        }

        if (recording)
        {
            StopRecord();
        }

        if (length == 0 || replaying)
        {
            return;
        }

        ResetAndApplyTargetST();

        targetAnimator.gameObject.SetActive(true);
        currentFrame = 1;
        replaying = true;


        Debug.Log("Start replaying: currentFrame:" + currentFrame + ", currentLength:" + length + ", local:" +
                  (NetworkCalling.CallingPlayer == Networking.LocalPlayer));
        if (core.IsPaused && Networking.GetOwner(gameObject) == Networking.LocalPlayer)
        {
            Debug.Log("We are the owner so Play the player");
            float startTime = timeStamps[0];
            core.Play();
            core.Time = startTime;
            // If not-owner player C trigger replay and they don't set progress, their replay will stop immediately
            StartReplayBroadcast();
        }

        // core.Time
    }

    [NetworkCallable]
    public void StopReplay()
    {
        if (NetworkCalling.InNetworkCall)
        {
            Debug.Log(NetworkCalling.CallingPlayer != Networking.LocalPlayer
                ? "Received network event StopReplaying"
                : "Sent network event StopReplaying");
        }

        if (!replaying)
        {
            return;
        }


        Debug.Log("Stopping replaying: currentFrame:" + currentFrame + ", currentLength:" + length + ", local:" +
                  (NetworkCalling.CallingPlayer == Networking.LocalPlayer));
        if (Networking.GetOwner(gameObject) == Networking.LocalPlayer)
        {
            Debug.Log("we are the owner so pause the player");
            core.Pause();
        }

        replaying = false;
        justReplayed = true;
    }

    private void Update()
    {
        if ((core.IsPlaying && lastTickPaused) || !core.IsReady)
        {
            justReplayed = false;
        }

        bool urlChanged = core.Url != null && core.Url.Get() != "" && lastAvailableURL != core.Url.Get();
        bool stopped = core.Url == null;

        if ((urlChanged || stopped) && Networking.GetOwner(gameObject) == Networking.LocalPlayer)
        {
            if (recording)
            {
                StopRecordBroadcast();
            }

            if (replaying)
            {
                StopReplayBroadcast();
            }
        }

        if (core.IsReady && core.Url != null && core.Url.Get() != "")
        {
            lastAvailableURL = core.Url.Get();
        }

        lastTickPaused = core.IsPaused;

        GameObject go = targetAnimator.gameObject;
        if (go.activeSelf && !replaying && !justReplayed)
        {
            go.SetActive(false);
        }


        if (recording && core.IsPlaying && core.IsReady)
        {
            if (currentFrame >= maxRecordingFrames)
            {
                Debug.Log("Recording Length exceeded");
                StopRecord();
                return;
            }

            float time = core.Time;

            // Progress Check
            // Rollback to proper recorded frame
            while (currentFrame > 0 && timeStamps[currentFrame - 1] > time)
            {
                currentFrame--;
            }

            Record();
            currentFrame++;
        }
        else if (replaying && core.IsReady)
        {
            float time = core.Time;

            if (Networking.GetOwner(gameObject) == Networking.LocalPlayer)
            {
                // Check if the player set progress out of recorded range
                if (time < timeStamps[1])
                {
                    Debug.Log("progress is before first frame, so set the progress to first frame's timestamp");
                    currentFrame = 1;
                    core.Time = timeStamps[1];
                }
                else if (time > timeStamps[length - 1])
                {
                    Debug.Log("progress is after lastest frame, so set the progress latest frame's timestamp");
                    currentFrame = length - 1;
                    core.Time = timeStamps[length - 1];
                }
            }


            // Handle if player backs the progress
            if (timeStamps[currentFrame] > time)
            {
                while (currentFrame > 1 && timeStamps[currentFrame - 1] > time)
                {
                    currentFrame--;
                }
            }

            while (currentFrame < length && timeStamps[currentFrame] < time)
            {
                currentFrame++;
            }

            if (Networking.GetOwner(gameObject) == Networking.LocalPlayer)
            {
                // Handle if the player drag the progress
                if (currentFrame > 0)
                {
                    if (timeStamps[currentFrame] - time > MinJumpInterval)
                    {
                        Debug.Log(
                            "next frame is much later than current progress so we jump to next frame's timestamp");
                        core.Time = timeStamps[currentFrame];
                    }
                }
            }


            float lerpFactor = GetLerpFactor(timeStamps[currentFrame - 1], timeStamps[currentFrame], time);
            for (int i = 0; i < BoneCount; i++)
            {
                lerpedSourceTs[i] = Vector3.LerpUnclamped(sourceTs[currentFrame - 1][i], sourceTs[currentFrame][i],
                    lerpFactor);
                lerpedSourceQs[i] = Quaternion.SlerpUnclamped(sourceQs[currentFrame - 1][i],
                    sourceQs[currentFrame][i], lerpFactor);
            }

            lerpedSourceT_Root = Vector3.LerpUnclamped(sourceT_Root[currentFrame - 1], sourceT_Root[currentFrame],
                lerpFactor);
            lerpedSourceQ_Root = Quaternion.SlerpUnclamped(sourceQ_Root[currentFrame - 1],
                sourceQ_Root[currentFrame], lerpFactor);

            ApplyTargetTQ();
            if (currentFrame >= length && Networking.GetOwner(gameObject) == Networking.LocalPlayer)
            {
                Debug.Log("Replaying Length exceeded");
                StopReplayBroadcast();
            }
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (recording)
        {
            StartRecordBroadcast();
        }
    }

    // calibrate player bone axes
    public void Calibrate()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        Debug.Log($"[MotionTracker] Calibrate {player.displayName}");

        // Update source
        calibrateT_Root = player.GetPosition();
        calibrateQ_Root = player.GetRotation();
        for (int i = 0; i < BoneCount; i++)
        {
            calibrateTs[i] = player.GetBonePosition((HumanBodyBones)i);
            calibrateQs[i] = player.GetBoneRotation((HumanBodyBones)i);
        }

        CalibrateQ(true);
        CalibrateScale();
        calibrated = true;
    }


    private float GetLerpFactor(float t1, float t2, float rt)
    {
        float delta = t2 - t1;
        if (Mathf.Abs(delta) < 0.0001f) return 0f; // 避免除零
        return (rt - t1) / delta;
    }

    // call this if you change the target

    public void InitializeTarget()
    {
        if (!Utilities.IsValid(targetAnimator))
            return;
        targetRoot = targetAnimator.transform;
        targetBones = new Transform[HumanTrait.BoneCount];
        for (int i = 0; i < HumanTrait.BoneCount; i++)
            targetBones[i] = targetAnimator.GetBoneTransform((HumanBodyBones)i);

        calibrateT_Root = targetRoot.position;
        calibrateQ_Root = targetRoot.rotation;

        for (int i = 0; i < BoneCount; i++)
            if (targetBones[i])
            {
                calibrateTs[i] = targetBones[i].position;
                calibrateQs[i] = targetBones[i].rotation;
            }

        CalibrateQ(true);
        CalibrateScale();

        targetUnitScale = targetRoot.localScale / localSourceScale;
        for (int i = 0; i < BoneCount; i++)
            if (targetBones[i])
            {
                targetPostQs[i] = localSourcePostQs[i];
                targetRestLQs[i] = targetBones[i].localRotation;
            }
    }

    Quaternion zeroQuat = new Quaternion(0, 0, 0, 0);

    int[] indexFingerBones = new int[]
    {
        LeftIndexProximal, LeftIndexIntermediate, LeftIndexDistal,
        RightIndexProximal, RightIndexIntermediate, RightIndexDistal
    };

    void ApplyTargetQ()
    {
        var preQ = targetRoot.rotation * Quaternion.Inverse(sourceRoot.rotation);
        for (int i = 0; i < BoneCount; i++)
        {
            var q = lerpedSourceQs[i] * currentSourcePostQs[i];
            if (!zeroQuat.Equals(q))
            {
                if (targetBones[i])
                    targetBones[i].rotation = preQ * q * Quaternion.Inverse(targetPostQs[i]);
            }
        }

        // propagate finger rotations
        foreach (var IndexFinger in indexFingerBones)
        {
            var MiddleFinger = IndexFinger - LeftIndexProximal + LeftMiddleProximal;
            var RingFinger = IndexFinger - LeftIndexProximal + LeftRingProximal;
            var LittleFinger = IndexFinger - LeftIndexProximal + LeftLittleProximal;
            if (zeroQuat.Equals(currentSourcePostQs[IndexFinger]) || !targetBones[IndexFinger])
                continue;
            if (zeroQuat.Equals(currentSourcePostQs[MiddleFinger]))
                targetBones[MiddleFinger].localRotation = targetBones[IndexFinger].localRotation;
            if (zeroQuat.Equals(currentSourcePostQs[RingFinger]))
                targetBones[RingFinger].localRotation = targetBones[MiddleFinger].localRotation;
            if (zeroQuat.Equals(currentSourcePostQs[LittleFinger]))
                targetBones[LittleFinger].localRotation = targetBones[RingFinger].localRotation;
            if (zeroQuat.Equals(currentSourcePostQs[RingFinger]))
                targetBones[RingFinger].localRotation = targetBones[LittleFinger].localRotation;
        }
    }

    void ApplyTargetTQ()
    {
        ApplyTargetQ();
        var hipsLT = sourceRoot.InverseTransformPoint(lerpedSourceTs[Hips]);
        targetBones[Hips].position = targetRoot.position + targetRoot.rotation * hipsLT;
    }

    void ResetAndApplyTargetST()
    {
        for (int i = 0; i < BoneCount; i++)
            if (targetBones[i])
                targetBones[i].localRotation = targetRestLQs[i];
        targetRoot.localScale = currentSourceScale * targetUnitScale;

    }

    void CalibrateScale()
    {
        localSourceScale = Mathf.Max(EstimateHipsH(LeftUpperLeg, LeftLowerLeg, LeftFoot),
            EstimateHipsH(RightUpperLeg, RightLowerLeg, RightFoot));
    }

    float EstimateHipsH(int UpperLeg, int LowerLeg, int Foot)
    {
        var hipsQ = calibrateQs[Hips] * localSourcePostQs[Hips];
        var upperLegQ = calibrateQs[UpperLeg] * localSourcePostQs[UpperLeg];
        var lowerLegQ = calibrateQs[LowerLeg] * localSourcePostQs[LowerLeg];
        var hipsV = calibrateTs[UpperLeg] - calibrateTs[Hips];
        var upperLegV = calibrateTs[LowerLeg] - calibrateTs[UpperLeg];
        var lowerLegV = calibrateTs[Foot] - calibrateTs[LowerLeg];
        return Vector3.Dot(hipsQ * Vector3.down, hipsV)
               + Vector3.Dot(upperLegQ * Vector3.down, upperLegV)
               + Vector3.Dot(lowerLegQ * Vector3.down, lowerLegV)
               + Vector3.Dot(calibrateQ_Root * Vector3.down,
                   calibrateT_Root - calibrateTs[Foot]);
    }

    // neutral pose of mixamo rig
    Quaternion upperLegPreQ = Quaternion.Euler(-30, 0, 0);
    Quaternion lowerLegPreQ = Quaternion.Euler(+80, 0, 0);
    Quaternion footPreQ = Quaternion.Euler(+20, 0, 0);

    Quaternion leftUpperArmPreQ = Quaternion.AngleAxis(new Vector3(0, +30, +40).magnitude,
        new Vector3(0, +30, +40).normalized);

    Quaternion rightUpperArmPreQ = Quaternion.AngleAxis(new Vector3(0, -30, -40).magnitude,
        new Vector3(0, -30, -40).normalized);

    Quaternion leftLowerArmPreQ = Quaternion.Euler(0, +80, 0);
    Quaternion rightLowerArmPreQ = Quaternion.Euler(0, -80, 0);
    Quaternion leftProximalPreQ = Quaternion.Euler(0, 0, +35);
    Quaternion rightProximalPreQ = Quaternion.Euler(0, 0, -35);

    Quaternion leftThumbProximalPreQ = Quaternion.AngleAxis(new Vector3(-10, +60, +20).magnitude,
        new Vector3(-10, +60, +20).normalized);

    Quaternion rightThumbProximalPreQ = Quaternion.AngleAxis(new Vector3(-10, -60, -20).magnitude,
        new Vector3(-10, -60, -20).normalized);

    // empirical weights
    const float upperArmQLerp = 0.3f;
    const float lowerArmQLerp = 0.7f;
    const float footZLerp = 0.7f;

    void CalibrateQ(bool strict)
    {
        if (strict)
            for (int i = 0; i < BoneCount; i++)
                localSourcePostQs[i] = new Quaternion(0, 0, 0, 0);

        CalibrateQ_Backbone();
        CalibrateQ_Head(strict);
        CalibrateQ_Leg(LeftUpperLeg, LeftLowerLeg, LeftFoot, LeftToes, strict);
        CalibrateQ_Leg(RightUpperLeg, RightLowerLeg, RightFoot, RightToes, strict);
        CalibrateQ_Arm(LeftShoulder, LeftUpperArm, LeftLowerArm, LeftHand,
            LeftIndexProximal, LeftMiddleProximal, LeftRingProximal, LeftLittleProximal,
            Vector3.left, leftUpperArmPreQ, leftLowerArmPreQ);
        CalibrateQ_Arm(RightShoulder, RightUpperArm, RightLowerArm, RightHand,
            RightIndexProximal, RightMiddleProximal, RightRingProximal, RightLittleProximal,
            Vector3.right, rightUpperArmPreQ, rightLowerArmPreQ);

        CalibrateQ_Finger(LeftHand, LeftThumbProximal, LeftThumbIntermediate, LeftThumbDistal,
            Vector3.left, leftThumbProximalPreQ);
        CalibrateQ_Finger(LeftHand, LeftIndexProximal, LeftIndexIntermediate, LeftIndexDistal,
            Vector3.left, leftProximalPreQ);
        CalibrateQ_Finger(LeftHand, LeftMiddleProximal, LeftMiddleIntermediate, LeftMiddleDistal,
            Vector3.left, leftProximalPreQ);
        CalibrateQ_Finger(LeftHand, LeftRingProximal, LeftRingIntermediate, LeftRingDistal,
            Vector3.left, leftProximalPreQ);
        CalibrateQ_Finger(LeftHand, LeftLittleProximal, LeftLittleIntermediate, LeftLittleDistal,
            Vector3.left, leftProximalPreQ);

        CalibrateQ_Finger(RightHand, RightThumbProximal, RightThumbIntermediate, RightThumbDistal,
            Vector3.right, rightThumbProximalPreQ);
        CalibrateQ_Finger(RightHand, RightIndexProximal, RightIndexIntermediate, RightIndexDistal,
            Vector3.right, rightProximalPreQ);
        CalibrateQ_Finger(RightHand, RightMiddleProximal, RightMiddleIntermediate, RightMiddleDistal,
            Vector3.right, rightProximalPreQ);
        CalibrateQ_Finger(RightHand, RightRingProximal, RightRingIntermediate, RightRingDistal,
            Vector3.right, rightProximalPreQ);
        CalibrateQ_Finger(RightHand, RightLittleProximal, RightLittleIntermediate, RightLittleDistal,
            Vector3.right, rightProximalPreQ);
    }

    void CalibrateQ_Head(bool strict)
    {
        var noEye = Vector3.zero.Equals(calibrateTs[LeftEye]) ||
                    Vector3.zero.Equals(calibrateTs[RightEye]);
        if (strict)
        {
            var headX = calibrateTs[RightEye] - calibrateTs[LeftEye];
            var headY = calibrateQs[Head] * Vector3.up;
            var headZ = calibrateQ_Root * Vector3.forward;
            if (Vector3.zero.Equals(headX))
                headX = calibrateQs[Head] * Vector3.right;
            // TODO: fix the hack here
#if UDON
            // We don't need this
            // if (sourcePlayer != null && sourcePlayer.isLocal)
            // {
            // 	CalibrateQ_SnapRot(Head, Vector3.up, Vector3.forward,
            // 		sourcePlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
            // }
            // else
#endif
            if (noEye)
                CalibrateQ_SnapVec(Head, Vector3.up, Vector3.forward, headY, headZ);
            else
                CalibrateQ_SnapVec(Head, Vector3.right, Vector3.forward, headX, headZ);
        }

        if (!noEye)
        {
            var headQ = calibrateQs[Head] * localSourcePostQs[Head];
            // calibrate eyes from head
            CalibrateQ_SnapRot(LeftEye, Vector3.forward, Vector3.up, headQ);
            CalibrateQ_SnapRot(RightEye, Vector3.forward, Vector3.up, headQ);
        }
    }

    void CalibrateQ_Leg(int UpperLeg, int LowerLeg, int Foot, int Toes, bool strict)
    {
        var hipsQ = calibrateQs[Hips] * localSourcePostQs[Hips];
        var upperLegV = calibrateTs[LowerLeg] - calibrateTs[UpperLeg];
        var lowerLegV = calibrateTs[Foot] - calibrateTs[LowerLeg];
        var footV = calibrateTs[Toes] - calibrateTs[Foot];
        if (Vector3.zero.Equals(calibrateTs[Toes]))
            footV = Vector3.zero;

        // calibrate upperLeg from hips
        var upperLegQ_0 = QuatLookAt(hipsQ * upperLegPreQ, Vector3.down, upperLegV);
        var upperLegQ = CalibrateQ_SnapRot(UpperLeg, Vector3.down, Vector3.right, upperLegQ_0);
        // calibrate lowerLeg from upperLeg
        var lowerLegQ_0 = QuatLookAt(upperLegQ * lowerLegPreQ, Vector3.down, lowerLegV);
        var lowerLegQ = CalibrateQ_SnapRot(LowerLeg, Vector3.down, Vector3.right, lowerLegQ_0);
        if (strict)
        {
            var grounded = true; // TODO
            // calibrate foot from lowerLeg & toes & ground plane
            var footZ = Vector3.Lerp(lowerLegQ * Vector3.forward, footV.normalized, footZLerp);

            var footY = (grounded ? calibrateQ_Root : lowerLegQ) * Vector3.up;
            CalibrateQ_FreeVec(Foot, Vector3.up, Vector3.forward, footY, footZ);
        }
    }

    void CalibrateQ_Backbone()
    {
        var hipsV = calibrateTs[Spine] - calibrateTs[Hips];
        var spineV = calibrateTs[Chest] - calibrateTs[Spine];
        var chestV =
            calibrateTs[Neck] -
            calibrateTs[Chest]; // this is approximation due to possible upperChest
        var neckV = calibrateTs[Head] - calibrateTs[Neck];
        hipsV += spineV * 0.01f; // take care of zero-length hips bone

        // calibrate hips & chest from lower & upper triangles
        var hipsQ = CalibrateQ_SnapVec(Hips, Vector3.up, Vector3.right,
            hipsV, calibrateTs[RightUpperLeg] - calibrateTs[LeftUpperLeg]);
        var chestQ = CalibrateQ_SnapVec(Chest, Vector3.up, Vector3.right,
            chestV, calibrateTs[RightShoulder] - calibrateTs[LeftShoulder]);
        // calibrate spine from hips & chest
        var spineQ_0 = QuatLookAt(hipsQ, Vector3.up, spineV);
        var spineQ_1 = QuatLookAt(chestQ, Vector3.up, spineV);
        var spineQ = CalibrateQ_SnapRot(Spine, Vector3.up, Vector3.right, Quaternion.Slerp(spineQ_0, spineQ_1, 0.5f));
        // calibrate neck from chest
        var neckQ_0 = QuatLookAt(chestQ, Vector3.up, neckV);
        var neckQ = CalibrateQ_SnapRot(Neck, Vector3.up, Vector3.right, neckQ_0);
    }

    void CalibrateQ_Arm(int Shoulder, int UpperArm, int LowerArm, int Hand,
        int IndexProximal, int MiddleProximal, int RingProximal, int LittleProximal,
        Vector3 axisOut, Quaternion upperArmPreQ, Quaternion lowerArmPreQ)
    {
        if (Vector3.zero.Equals(calibrateTs[RingProximal]))
        {
            RingProximal = LittleProximal;
            if (Vector3.zero.Equals(calibrateTs[RingProximal]))
                RingProximal = MiddleProximal;
        }

        var chestQ = calibrateQs[Chest] * localSourcePostQs[Chest];
        var shoulderV = calibrateTs[UpperArm] - calibrateTs[Shoulder];
        var upperArmV = calibrateTs[LowerArm] - calibrateTs[UpperArm];
        var lowerArmV = calibrateTs[Hand] - calibrateTs[LowerArm];
        var indexV = calibrateTs[IndexProximal] - calibrateTs[Hand];
        var ringV = calibrateTs[RingProximal] - calibrateTs[Hand];
        var noFinger = Vector3.zero.Equals(calibrateTs[IndexProximal]) ||
                       Vector3.zero.Equals(calibrateTs[RingProximal]);

        // calibrate hand from hand triangle
        var handQ = CalibrateQ_SnapVec(Hand, axisOut, Vector3.forward, indexV + ringV, indexV - ringV);
        // calibrate shoulder from chest
        var shoulderQ_0 = QuatLookAt(chestQ, axisOut, shoulderV);
        var shoulderQ = CalibrateQ_SnapRot(Shoulder, axisOut, Vector3.forward, shoulderQ_0);
        // calibrate upperArm from untwisted shoulder & lowerArm
        var lowerArmQ_1 = QuatLookAt(handQ, axisOut, lowerArmV);
        var upperArmQ_0 = QuatLookAt(shoulderQ_0 * upperArmPreQ, axisOut, upperArmV);
        var upperArmQ_1 = QuatLookAt(lowerArmQ_1 * Quaternion.Inverse(lowerArmPreQ), axisOut, upperArmV);
        var upperArmQ = CalibrateQ_SnapRot(UpperArm, axisOut, Vector3.forward,
            noFinger ? upperArmQ_0 : Quaternion.Slerp(upperArmQ_0, upperArmQ_1, upperArmQLerp));
        // calibrate lowerArm from upperArm & hand
        var lowerArmQ_0 = QuatLookAt(upperArmQ * lowerArmPreQ, axisOut, lowerArmV);
        // calibrate hand from lowerArm when finger is missing
        var lowerArmQ = CalibrateQ_SnapRot(LowerArm, axisOut, Vector3.forward,
            noFinger ? lowerArmQ_0 : Quaternion.Slerp(lowerArmQ_0, lowerArmQ_1, lowerArmQLerp));
        if (noFinger)
            handQ = CalibrateQ_SnapRot(Hand, axisOut, Vector3.forward, lowerArmQ);
    }

    void CalibrateQ_Finger(int Hand, int Proximal, int Intermediate, int Distal,
        Vector3 axisOut, Quaternion proximalPreQ)
    {
        if (Vector3.zero.Equals(calibrateTs[Proximal]))
            return;
        var handQ = calibrateQs[Hand] * localSourcePostQs[Hand];
        var proximalV = calibrateTs[Intermediate] - calibrateTs[Proximal];
        if (Vector3.zero.Equals(calibrateTs[Intermediate]))
            proximalV = Vector3.zero;

        // calibrate proximal from hand
        var proximalQ_0 = QuatLookAt(handQ * proximalPreQ, axisOut, proximalV);
        var proximalQ = CalibrateQ_FreeSnapRot(Proximal, axisOut, Vector3.up, proximalQ_0);
        // reuse proximal calibration on intermediate & distal
        if (!Vector3.zero.Equals(calibrateTs[Intermediate]))
            localSourcePostQs[Intermediate] = localSourcePostQs[Proximal];
        if (!Vector3.zero.Equals(calibrateTs[Distal]))
            localSourcePostQs[Distal] = localSourcePostQs[Proximal];
    }

    Vector3 SnapAxis(Vector3 vec)
    {
        var vecAbs = Vector3.Max(vec, -vec);
        var maxAbs = Mathf.Max(vecAbs.x, vecAbs.y, vecAbs.z);
        return maxAbs == vecAbs.x ? Vector3.right * Mathf.Sign(vec.x) :
            maxAbs == vecAbs.y ? Vector3.up * Mathf.Sign(vec.y) : Vector3.forward * Mathf.Sign(vec.z);
    }

    Quaternion QuatLookAt(Quaternion q, Vector3 d, Vector3 v)
    {
        return Quaternion.FromToRotation(q * d, v) * q;
    }

    Quaternion CalibrateQ_LocalLookAt(int boneId, Vector3 axis0, Vector3 axis1, Vector3 lvec0, Vector3 lvec1)
    {
        var postQ = Quaternion.LookRotation(lvec0, lvec1) * Quaternion.Inverse(Quaternion.LookRotation(axis0, axis1));
        localSourcePostQs[boneId] = postQ;
        return calibrateQs[boneId] * postQ;
    }

    Quaternion CalibrateQ_FreeVec(int boneId, Vector3 axis0, Vector3 axis1, Vector3 vec0, Vector3 vec1)
    {
        var inv = Quaternion.Inverse(calibrateQs[boneId]);
        return CalibrateQ_LocalLookAt(boneId, axis0, axis1, inv * vec0, inv * vec1);
    }

    Quaternion CalibrateQ_SnapVec(int boneId, Vector3 axis0, Vector3 axis1, Vector3 vec0, Vector3 vec1)
    {
        var inv = Quaternion.Inverse(calibrateQs[boneId]);
        var lvec0 = SnapAxis(inv * vec0);
        var lvec1 = SnapAxis(Vector3.ProjectOnPlane(inv * vec1, lvec0));
        return CalibrateQ_LocalLookAt(boneId, axis0, axis1, lvec0, lvec1);
    }

    Quaternion CalibrateQ_SnapRot(int boneId, Vector3 axis0, Vector3 axis1, Quaternion rot)
    {
        var inv = Quaternion.Inverse(calibrateQs[boneId]);
        rot = inv * rot;
        var lvec0 = SnapAxis(rot * axis0);
        var lvec1 = SnapAxis(Quaternion.FromToRotation(rot * axis0, lvec0) * (rot * axis1));
        return CalibrateQ_LocalLookAt(boneId, axis0, axis1, lvec0, lvec1);
    }

    Quaternion CalibrateQ_FreeSnapRot(int boneId, Vector3 axis0, Vector3 axis1, Quaternion rot)
    {
        var inv = Quaternion.Inverse(calibrateQs[boneId]);
        rot = inv * rot;
        var lvec0 = (rot * axis0);
        var lvec1 = SnapAxis(rot * axis1);
        return CalibrateQ_LocalLookAt(boneId, axis0, axis1, lvec0, lvec1);
    }


    public void StartRecordBroadcast()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StartRecord));
    }

    public void StopRecordBroadcast()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StopRecord));
    }

    public void StartReplayBroadcast()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StartReplay));
    }

    public void StopReplayBroadcast()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StopReplay));
    }

    const int Hips = (int)HumanBodyBones.Hips;
    const int LeftUpperLeg = (int)HumanBodyBones.LeftUpperLeg;
    const int RightUpperLeg = (int)HumanBodyBones.RightUpperLeg;
    const int LeftLowerLeg = (int)HumanBodyBones.LeftLowerLeg;
    const int RightLowerLeg = (int)HumanBodyBones.RightLowerLeg;
    const int LeftFoot = (int)HumanBodyBones.LeftFoot;
    const int RightFoot = (int)HumanBodyBones.RightFoot;
    const int Spine = (int)HumanBodyBones.Spine;
    const int Chest = (int)HumanBodyBones.Chest;
    const int UpperChest = (int)HumanBodyBones.UpperChest;
    const int Neck = (int)HumanBodyBones.Neck;
    const int Head = (int)HumanBodyBones.Head;
    const int LeftShoulder = (int)HumanBodyBones.LeftShoulder;
    const int RightShoulder = (int)HumanBodyBones.RightShoulder;
    const int LeftUpperArm = (int)HumanBodyBones.LeftUpperArm;
    const int RightUpperArm = (int)HumanBodyBones.RightUpperArm;
    const int LeftLowerArm = (int)HumanBodyBones.LeftLowerArm;
    const int RightLowerArm = (int)HumanBodyBones.RightLowerArm;
    const int LeftHand = (int)HumanBodyBones.LeftHand;
    const int RightHand = (int)HumanBodyBones.RightHand;
    const int LeftToes = (int)HumanBodyBones.LeftToes;
    const int RightToes = (int)HumanBodyBones.RightToes;
    const int LeftEye = (int)HumanBodyBones.LeftEye;
    const int RightEye = (int)HumanBodyBones.RightEye;
    const int Jaw = (int)HumanBodyBones.Jaw;
    const int LeftThumbProximal = (int)HumanBodyBones.LeftThumbProximal;
    const int LeftThumbIntermediate = (int)HumanBodyBones.LeftThumbIntermediate;
    const int LeftThumbDistal = (int)HumanBodyBones.LeftThumbDistal;
    const int LeftIndexProximal = (int)HumanBodyBones.LeftIndexProximal;
    const int LeftIndexIntermediate = (int)HumanBodyBones.LeftIndexIntermediate;
    const int LeftIndexDistal = (int)HumanBodyBones.LeftIndexDistal;
    const int LeftMiddleProximal = (int)HumanBodyBones.LeftMiddleProximal;
    const int LeftMiddleIntermediate = (int)HumanBodyBones.LeftMiddleIntermediate;
    const int LeftMiddleDistal = (int)HumanBodyBones.LeftMiddleDistal;
    const int LeftRingProximal = (int)HumanBodyBones.LeftRingProximal;
    const int LeftRingIntermediate = (int)HumanBodyBones.LeftRingIntermediate;
    const int LeftRingDistal = (int)HumanBodyBones.LeftRingDistal;
    const int LeftLittleProximal = (int)HumanBodyBones.LeftLittleProximal;
    const int LeftLittleIntermediate = (int)HumanBodyBones.LeftLittleIntermediate;
    const int LeftLittleDistal = (int)HumanBodyBones.LeftLittleDistal;
    const int RightThumbProximal = (int)HumanBodyBones.RightThumbProximal;
    const int RightThumbIntermediate = (int)HumanBodyBones.RightThumbIntermediate;
    const int RightThumbDistal = (int)HumanBodyBones.RightThumbDistal;
    const int RightIndexProximal = (int)HumanBodyBones.RightIndexProximal;
    const int RightIndexIntermediate = (int)HumanBodyBones.RightIndexIntermediate;
    const int RightIndexDistal = (int)HumanBodyBones.RightIndexDistal;
    const int RightMiddleProximal = (int)HumanBodyBones.RightMiddleProximal;
    const int RightMiddleIntermediate = (int)HumanBodyBones.RightMiddleIntermediate;
    const int RightMiddleDistal = (int)HumanBodyBones.RightMiddleDistal;
    const int RightRingProximal = (int)HumanBodyBones.RightRingProximal;
    const int RightRingIntermediate = (int)HumanBodyBones.RightRingIntermediate;
    const int RightRingDistal = (int)HumanBodyBones.RightRingDistal;
    const int RightLittleProximal = (int)HumanBodyBones.RightLittleProximal;
    const int RightLittleIntermediate = (int)HumanBodyBones.RightLittleIntermediate;
    const int RightLittleDistal = (int)HumanBodyBones.RightLittleDistal;
    const int BoneCount = (int)HumanBodyBones.LastBone;
}