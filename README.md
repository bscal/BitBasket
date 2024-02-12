
## BitBasket ##
Program that simulates bits falling into a cup from your Twitch chat.

You can use your broadcast software to scale and crop the app. And the app will
also run in the background.

### Running ###
1. Download latest version from releases.
2. Run the program and look to the top left.
3. Insert your twitch username
4. Click "Connect to Channel" button
5. This will popup Twitch autherization web page
6. That's it

Your authorization and username will save. You may need to reauthorize if the code
expires or possibly on username change.

Uses port 8080 to authorize the app.

### Usage ###
Chat command `!bb test <amount>` will test from twitch chat with custom amount.

Otherwise any message with bits should queue up an order of bits to spawn.

Setting "Save Bits on Close" to on will save the current bits when you close the app
and load them on next start.

### Dev ###
Here are the dependencies:
* Godot Engine version 4.2.1.stable.mono
* TwitchLib 3.5.3 - I guess DotNet or Nuget will install this for you
