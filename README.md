# Stream Deck - Clockify

![Clockify + Elgato Stream Deck](Docs/Clockify-GitHub-Banner.png)

This plugin allows you to track, start and stop Clockify timers on your [Elgato Stream Deck](https://www.elgato.com/en/stream-deck).

## Download

Until the plugin is available in the [Stream Deck Store](https://apps.elgato.com/plugins), you can download a copy from the [GitHub release pages](https://github.com/eXpl0it3r/streamdeck-clockify/releases/latest).

## Setup

- **Basic**
  - **Title:** Override the title being set by the plugin, leave empty otherwise
  - **API Key:** *(required)* Provide your 48 characters long [Clockify API Key](https://clockify.me/user/settings), which is required for the plugin to work
  - **Workspace Name:** *(required)* Write the name of the workspace you want to run/track timers in
  - **Project Name:** *(optional)* Provide the name of an existing project to run/track a timer for
  - **Task Name:** *(optional)* Set the name of the project-specific task
  - **Timer Name:** *(optional)* Specify a name for the timer you want to run/track
  - **Tags:** *(optional)* Provide a comma separated list of tags to be assigned to the timer
    - Note: If your tag contains a comma (WHY?!) use a backslash to escape it, e.g. `tag1,tag\,2,tag3`
  - **Billable** *(optional)* Check or uncheck the box to run the timer as billable or non-billable 
- **Advanced**
  - **Client Name:** *(optional)* Set the client name assigned to the specified project
  - **Title Format:** *(optional)* Specify the format for the title to be displayed on the button.
    - This can include any of:
      - `{workspaceName}` : The workspace name
      - `{projectName}` : The project name
      - `{taskName}` : The task name 
      - `{timerName}` : The timer name
      - `{clientName}` : The client name
      - `{timer}` : The current timer value when running. Blank when not running
  - **Server Url:** *(required)* Change from the *default* URL to the API URL of your own/company instance
    - Note: Starting with V1.11 the URL should *not* end with `/v1` 

https://user-images.githubusercontent.com/920861/132741561-6f9f3ff0-a920-408d-8279-579840ce0a6b.mp4

## FAQ / Troubleshooting

- Why am I getting a yellow triangle when pressing the button?
  - Your API Key is likely incorrect
  - If you have clients assigned to your project, make sure they're configured in the Stream Deck
- Why am I not seeing the running timer on my button?
  - Make sure you haven't set a title, as this will override any other content
  - Make sure the API Key, Workspace name and optional the project and timer name
- Why are my tags missing on the timer?
  - Make sure the tags have been created through the web app, as the plugin doesn't create them
  - Make sure the tag names match and tags with commas (WHY!?!) are escape with a backslash, e.g. `tag1,tag\,2,tag3`
- Why does the timer always start with a negative number?
  - This can happen when your local computer time isn't in sync with the Clockify server time
  - Make sure you synchronize your clock with a time-server
- Why does it always take some seconds to show the timer running?
  - Due to API rate limits, there's some magical caching going on, leading to certain delays 
- Why can't I select my Workspace and Project in a dropdown menu?
  - Because I was lazy 😅
- Where can I find the logs?
  - Windows: `%appdata%\Elgato\StreamDeck\Plugins\dev.duerrenberger.clockify.sdPlugin\Windows\pluginlog.log`
  - macOS: `~/Library/Application Support/com.elgato.StreamDeck/Plugins/dev.duerrenberger.clockify.sdPlugin/macOS/pluginlog.log`
- IT DOESN'T WORK, WHY?!?
  - Feel free to open a [GitHub issue](https://github.com/eXpl0it3r/streamdeck-clockify/issues) or ping me on [Bluesky](https://bsky.app/profile/darkcisum.bsky.social) or [Twitter](https://twitter.com/DarkCisum)

## Credits

- Feel free to star this repository and follow me on [Bluesky](https://bsky.app/profile/darkcisum.bsky.social) or [Twitter](https://twitter.com/DarkCisum)
- Thanks to [Bar Raiders](https://barraider.com/) for the great tooling and community
- Shout-out to [Hugh Macdonald](https://github.com/HughMacdonald) for adding the text formatting feature!
- Took some inspiration from the [Toggl plugin](https://github.com/tobimori/streamdeck-toggl)
- Using a CC0 licensed [Timer image](https://www.svgrepo.com/svg/23258/timer) for the Time Tracking category
- Talking to Clockify with the [Clockify.Net](https://github.com/Morasiu/Clockify.Net) library
- And thanks to [Clockify](https://clockify.me/) for providing an excellent time tracking tool for free

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details