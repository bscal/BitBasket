
## BitBasket ##
Program that simulates bits falling into a cup from your Twitch chat.

### Running ###
1. Download latest version from releases.
2. Insert your twitch username in top left and click
3. Click "Connect to Channel" button
4. Authorize the Twitch authorization web page

If you are using OBS to capture the window. Use game capture and "Enable Transparency".
The window can run in the background but minimizing will not render, for now if you try
to minimize, it will just return to window mode.

There are several settings in the Settings menu. You can enable/disable features, and
customize some of the bits physics.

Your authorization and username will save. You may need to reauthorize if the code
expires or possibly on username change.

Uses port 8080 to authorize the app.

Setting "Save Bits on Close" to on will save the current bits when you close the app
and load them on next start.

Broadcasters and Moderators have access to !bb test <amount> and !bb rain <level> test commands.

### Dev ###
Here are the dependencies:
* Godot Engine version 4.2.1.stable.mono
* TwitchLib 3.5.3 - I guess DotNet or Nuget will install this for you


<img width=300 height=300 src='https://github.com/bscal/BitBasket/assets/4869976/6a16ab33-c351-42db-a55d-2384adec6696'></img>
