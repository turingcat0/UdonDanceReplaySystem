# DanceReplaySystem

## Overview

DanceReplaySystem is an in-game motion recording and playback tool designed for VRChat worlds. Developed with UdonSharp, it provides dancers and performers with the ability to record their movements in real-time and replay them onto a pre-placed puppet avatar.

This system is especially useful for dance practice. When used in conjunction with a video player like the [Vizvid player](https://github.com/JLChnToZ/VVMW/), it allows players to simultaneously view a reference video and their own replayed performance. This creates a direct, side-by-side comparison, making it easier to identify and correct differences between their movements and the original choreography.

This project is partially derived from the foundational work of [UdonMotion](https://gitlab.com/lox9973/UdonMotion).

## Features

* **Record & Replay:** Easily record your avatar's movements and replay the performance on a separate puppet model.
* **Practice Aid:** Perfect for dancers who want to mirror and match choreography from a video source.

## Getting Started

### Prerequisites

Before you begin, please ensure you have the following software and packages installed. The system has been tested with these versions:

* **Unity:** `2022.3.22f1`
* **VRChat World SDK:** `3.8.1`
* **Vizvid Player** `1.4.8`

### Installation

1.  Navigate to the [**Releases**](https://github.com/turingcat0/UdonDanceReplaySystem/releases) page of this repository.
2.  Download the latest `.zip` file.
3.  Extract the contents of the downloaded file directly into the `Assets/` folder of your Unity project.

### Usage & Example

To see the system in action and understand how to integrate it into your own world, please check out the included example scene:

1.  In your Unity project, navigate to the Project window.
2.  Open the following scene file: `Assets/DanceReplaySystem/Example/DanceReplayExample.unity`

This scene provides a working demonstration of the recording and playback features.