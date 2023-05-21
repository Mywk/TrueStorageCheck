# TrueStorageCheck 💾

TrueStorageCheck is an open-source software tool designed to verify the authenticity and advertised capacity of various storage media. It works with a wide range of devices, from SD cards, internal hard drives, to USB and memory sticks.

TrueStorageCheck works similarly to H2testw with some key improvements - it is adapted for testing multiple devices simultaneously and provides faster results by terminating the test the moment a validation error occurs. It's lightweight and simple to use, making it an essential tool for anyone in need of reliable storage verification. I originally wrote this for fun after being tasked with testing quite a few potentially fake USB sticks, the project kept growing during last years and is a reliable tool so so I'm making it open-source.

## Features

- Support for various storage media: SD cards, internal hard drives, USB sticks, memory sticks, etc..
- Multi-device testing: Capable of verifying multiple storage devices simultaneously
- Fast error detection: The test stops as soon as a validation error is detected
- Lightweight and portable (no installation required)
- Simple to use: Clear and intuitive user interface (I hope, suggestions are always welcome)
- Multilanguage (feel free to add your own)

## TODO

- Log files
- Multi-threaded data generation
- Check if drive is empty before testing (GUI)
- CLI

## How to Use

[More information, download, demo and FAQ can be found here.](https://mywk.net/software/true-storage-check) (soon)

## GUI

### Arguments

You can pass several arguments when starting up (case insensitive):

* NoMaxDevices - Ignore maximum devices limit
* Dark - Force dark mode
* Light - Force light mode
* Noblur - Prevent the blurring effect, just in case your computer really doesn't like it

## Warning

Perform this only on drives you have no important data on, always backup your files before starting a test.

## License

[TrueStorageCheck is licensed under the EUPL1.2](LICENSE)

## Contribution

Contributions are welcome. Feel free to fork the project, make your changes, and submit a pull request.

## Disclaimer

Please note that while TrueStorageCheck is a reliable tool for identifying fake or erroneous storage devices, it is not foolproof. Always ensure to buy storage devices from reputable sources.
