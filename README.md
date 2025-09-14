Fighter Jet Flight Simulator
============================

This project is a  flight simulator that uses CesiumJS to render the
entire planet in 3D and a fighter jet model as the aircraft. It’s designed
to be lightweight, fun to play with, and easy to expand.

-------------------------------------------------------
Features
-------------------------------------------------------
- Fly a fighter jet anywhere on Earth using Cesium’s 3D globe.
- Basic but responsive flight controls (pitch, roll, yaw, throttle).
- Multiple camera views
- Terrain, imagery, and atmospheric rendering through Cesium.

-------------------------------------------------------
Tech Stack
-------------------------------------------------------
- CesiumJS for 3D globe rendering
- C++ for flight and camera logic
- glTF for the fighter jet model
- HTML / CSS for the HUD and menus

-------------------------------------------------------
Getting Started
-------------------------------------------------------
1. Install Node.js (v16 or newer is recommended).
2. Clone the repository:
   git clone https://github.com/your-username/flight-simulator.git
3. Navigate into the folder and install dependencies:
   cd flight-simulator
   npm install
4. Start the development server:
   npm run start
5. Open http://localhost:3000 in your browser.

-------------------------------------------------------
Controls
-------------------------------------------------------
Space / Ctrl       - Throttle up / down
A / D              - Roll left / right
S / W              - Pitch up / down
Arrow Left / Right - Yaw left / right
1/2/3/4            - Switch camera
J                  - Turn on Altitude Hold (use once in air)

-------------------------------------------------------
Cesium Setup
-------------------------------------------------------
You’ll need a Cesium Ion account and API token for terrain and imagery:

1. Sign up at https://cesium.com/ion
2. Generate an access token.
3. Add it to your environment variables:
   export CESIUM_ION_TOKEN=your_token_here

-------------------------------------------------------
License
-------------------------------------------------------
This project is released under the MIT License.

-------------------------------------------------------
Future Updates
-------------------------------------------------------

- Realistic Collisions
- Better Terrain Generation
- Improved Camera System
-------------------------------------------------------
