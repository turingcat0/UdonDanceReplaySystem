using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

public class DanceReplayUIHandler : UdonSharpBehaviour
{
    public DanceReplay danceReplay;
    public GameObject toggleRecord;
    public GameObject toggleReplay;
    public GameObject calibration;

    public Text statusUI;
    public Text playerNameUI;

    public Color recalibrationColor;

    private Text recordTextUI;

    private Text replayTextUI;

    private Text calibrationTextUI;


    void Start()
    {
        recordTextUI = toggleRecord.GetComponentInChildren<Text>();

        replayTextUI = toggleReplay.GetComponentInChildren<Text>();

        calibrationTextUI = calibration.GetComponentInChildren<Text>();
    }

    public void Update()
    {
        toggleRecord.SetActive(danceReplay.core.IsReady && danceReplay.calibrated);
        toggleReplay.SetActive(danceReplay.core.IsReady && danceReplay.calibrated);

        recordTextUI.text = danceReplay.recording ? "停止录制" : "开始录制";  // Stop recording, Start Recording
        replayTextUI.text = danceReplay.replaying ? "停止重放" : "开始重放";  // Stop replying,  Start Replaying

        if (statusUI)
        {
            if (danceReplay.recording)
            {
                statusUI.text = "当前正在录制动作"; // Currently recording
            }else if (danceReplay.replaying)
            {
                statusUI.text = "当前正在重放动作"; // Currently replaying
            }
            else
            {
                statusUI.text = "当前处于空闲状态"; // Idle
            }
        }

        if (playerNameUI)
        {
            if (danceReplay.recording || danceReplay.replaying)
            {
                playerNameUI.text = danceReplay.sourcePlayerName;
            }
            else
            {
                playerNameUI.text = "";
            }
        }
    }


    public void BroadcastCustomToggleRecording()
    {
        if (!danceReplay.recording)
        {
            danceReplay.StartRecordBroadcast();
        }
        else
        {
            danceReplay.StopRecordBroadcast();
        }
    }

    public void BroadcastCustomToggleReplay()
    {
        if (!danceReplay.replaying)
        {
            danceReplay.StartReplayBroadcast();
        }
        else
        {
           danceReplay.StopReplayBroadcast();
        }

    }


    public void Calibrate()
    {
        danceReplay.Calibrate();
        calibrationTextUI.text = "重新校准";
        calibrationTextUI.color = recalibrationColor;
    }
}