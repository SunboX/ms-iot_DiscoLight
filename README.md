# "Disco Light" app for the Microsoft Windows 10 IoT Core

This app was written using C#. It shows a solid color on the attached display. Also it provides a REST-ful API to change the color shown.

The goal of this project is to put the image unit of a old used display infront of a normal ceiling lamp to dynamically change the light color and brightness.

If you run this app on your Raspberry Pi 2, it will start up a web server listening on port `8081`. The default color is `White` and the current IP address, the server is listening to, is presented in the bottom right corner of the attached screen.

To change the screens color, simply call:
`http://YOUR_ID_ADRESS:8081/?color=0095DD`
