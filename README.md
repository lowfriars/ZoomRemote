# ZoomRemote
An Android app to remotely control a [Zoom H2n](https://zoomcorp.com/en/us/handheld-recorders/handheld-recorders/h2n-handy-recorder/)
digital audio recorder via bluetooth.

## Getting Started

The recorder has a socket for a wired remote control - a 2.5mm TRRS jack. Control is by means of a 2400 baud serial 
connection and the corresponding plug wired:
* Tip:  RX
* Next Ring: TX
* Next Ring: Ground
* Sleeve: 3.3V power (out)

TX, RX and Ground can be connected to a readily-available serial Bluetooth module, such as the [HC-06](https://components101.com/wireless/hc-06-bluetooth-module-pinout-datasheet),
though as these modules require 5V power, an additional power supply will be required. 
There are 3.3V Bluetooth modules, but the Zoom does not seem to have the capacity to power them.

The HC-06 will need to be configured for the correct baud rate (at least) and for this purpose it
will have to be connected to a serial port with 3.3V logic levels . It can either be programmed by using AT
commands, or more simply by using [this convenient utility](http://smarpl.com/content/bluetooth-module-hc04hc06-configuration-tool).

(More details of the harware to be provided)

### Prerequisites

The Android app is written in C# (for reasons that will be explained elsewhere) and requires
Visual Studio 2019 with the Xamarin extensions for Android if you want to tinker with it. 
Unfortunately, this requires a huge amount of disk space.

You will also, obviously, need an Android device running at least Android 5.1 and sufficient screen 
estate for the buttons.

### Installing

An apk file can be downloaded from the repository - it's not available in the Play store.
Further instructions on sideloading can be found [here](https://androidcommunity.com/how-to-sideloading-apps-on-your-android-device-20180417/).

### Usage
Once the HC-06 is correctly configured and wired to the Zoom recorder, the app can be launched.
Use the "scan..." button to select the correct Bluetooth device and pair it (if not previously paired).
Your choice will be remembered and used the next time the app is launched.

Initially the buttons will be dimmed and disabled, but when the app has successfully connected to the
recorder, the applicable buttons will be highlighted and enabled. Different buttons are available in 
Play mode and Record mode. 

(Image to be provided)

## Authors

* **Tim Dixon** 

See also the list of [contributors](https://github.com/your/project/contributors) who participated in this project.

## License

This project is licensed under the MIT License - see the [LICENSE.md](file://LICENSE.md) file for details

## Acknowledgments

Various people have details online of their efforts to decode the protocol used by the wired remote controls
and other projects to build hardware or software controllers. Some of those who have been most helpful include:
* [Christian Albert Meyer](https://christianalbertmeyer.wordpress.com/tag/zoom-h2n/)
* [Marcus Wohlschon](http://marcuswolschon.blogspot.com/2012/04/easterhegg-basel-2012.html)
* [This UK Blog](https://www.g7smy.co.uk/2017/04/hacking-the-zoom-h2n-remote/)
* [This German Blog](https://dreimeisen.de/?p=305)
