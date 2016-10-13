# Open source Lighthouse tracking

Github repository: <https://github.com/gevapatz/lighthouse>

For details: <http://making.do/lighthouse/>

## Directory layout

`Firmware` has firmware for the Arduino 101 (Intel Curie breakout board in an
Arduino form factor). To build, download the 
[Curie Open Development Kit](https://github.com/01org/CODK-A) 'A' tree, then
build and upload the firmware with:

`make convert-sketch SKETCH=Lighthouse.ino && make compile && make upload
SERIAL_PORT=/dev/ttyACM0`

The firmware will program the FPGA on the Lighthouse shield directly, so
you don't need a separate programming step for that.

`Hardware` has schematics and Gerber files for the Lighthouse tracking FPGA
shield and companion sensor modules, designed to attach to the shield with
solder tab to 2.54mm pitch connector flat flex cable. The Triad Semiconductor
TS3633 breakout boards work just fine with this shield if you don't want to
go cross-eyed building tiny sensor boards. 

Source files are available on CircuitMaker:

FPGA shield: <http://circuitmaker.com/Projects/Details/Geva-Patz/Lighthouse-tracking-FPGA-shield>

Sensor board: <http://circuitmaker.com/Projects/Details/Geva-Patz/Lighthouse-tracking-sensor-board>

`BLE Receiver` is a basic Bluetooth LE receiver for Windows in C#, which
reads output from the Curie and outputs it as a UDP stream.

`Unity` is a sample Unity project which takes in the UDP data stream and
uses it to manipulate a sphere in 3D space. You'll need to manually 
adjust the coordinates of your two lighthouses (auto-calibration coming
soon...)

`STL` contains the STLs and OpenSCAD source for a simple sensor holder for
three TS3633 demo boards from Triad Semiconductor. This is a very early 
prototype; more sensors in a move vareid configuration would track even better. 


## License

- Windows Bluetooth code is based on the demo code for the BGLib library, and
is thus licensed under the same MIT license

- All other software, hardware and 3D models are licensed under the 
Creative Commons Attribution 3.0 license [CC-BY-3.0](https://creativecommons.org/licenses/by/3.0/us/) 