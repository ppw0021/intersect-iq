# IntersectIQ🚦
*A mobile traffic intersection simulator and mapping prototype built in Unity.*

---

## Overview
**IntersectIQ** is a Unity-based prototype designed to simulate and visualise urban traffic intersections using real map data.  
It enables users to:
- Retrieve map data dynamically via the Google Maps API.
- Define and edit intersection layouts on a grid.
- Simulate traffic flow and behaviour across intersections.

This project was developed as part of the **COMP826 – Mobile Systems Development** paper at **Auckland University of Technology (AUT)**.

---

## Repository Contents
This repository contains:
- `intersectiq-unityapp/` — The Unity project folder with all scenes, scripts, and prefabs.  
- `Assets/Scenes/StartScreen.unity` — Entry point scene for running the simulation.  
- `Assets/Scripts/` — Core logic including map fetching, placement systems, and simulation controllers.  

> *Note:* The Google Maps API key required for map rendering is **not included in this repository**.  
> The key can be found in the accompanying **project report**.

---

## Installation & Setup

### Prerequisites
- **Unity Hub** installed.
- **Unity Editor 6000.1.7f1** installed via Unity Hub.
- Internet access (required for Google Maps API requests).

---

### 1. Clone the Repository
```bash
git clone https://github.com/ppw0021/intersect-iq.git
cd intersectiq-unityapp
```
### 2. Configure Environment Variables
Create a .env file in the /intersectiq-unityapp/ directory and paste the API key (found in the report):
`GOOGLE_MAPS_API_KEY=YOUR_API_KEY_HERE`
### 3. Open the Project in Unity
Open Unity Hub → Projects → Add project from disk.
Select the folder:
/intersectiq-unityapp/
Ensure that the Editor version is set to 6000.1.7f1.
### 4. Run the Prototype
1. In Unity, open the scene:
`Assets/Scenes/StartScreen.unity`
2. Switch to the Game tab.
3. From the Game dropdown, select the view Simulator.
4. Press Place to start the simulation. 
