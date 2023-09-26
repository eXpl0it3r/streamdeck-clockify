# Stream Deck - Clockify

![Clockify + Elgato Stream Deck](Docs/Clockify-GitHub-Banner.png)

This plugin allows you to track, start and stop Clockify timers on your [Elgato Stream Deck](https://www.elgato.com/en/stream-deck).

## Fork new features

I added 3 features :
- **Refresh rate** : Every 10 seconds instead of every second. I don't like poking an API each second. It's using a cache value to increment the second timer between each updates
- **Total week time** : I wanted to have the total week logged time, so here it is.
- **Total day time** : I wanted to have the current day logged time, so here it is.

I removed (because I don't use it):
- **Task name**
- **Timer name**
- **Task name**

## Download

You can download a copy from the [GitHub release pages](https://github.com/sponsCA/streamdeck-clockify/releases/latest).

## Setup

- **Basic**
  - **Title:** Override the title being set by the plugin, leave empty otherwise
  - **API Key:** *(required)* Provide your 48 characters long [Clockify API Key](https://clockify.me/user/settings), which is required for the plugin to work
  - **Workspace Name:** *(required)* Write the name of the workspace you want to run/track timers in
  - **Project Name:** *(optional)* Provide the name of an existing project to run/track a timer for
  - **Show week time:** *(optional)* False by default. If true, the timer value will be the total week time, and clicking on it will update it.
  - **Show current day time:** *(optional)* False by default. If true, the timer value will be the current day time, and clicking on it will update.
- **Advanced**
  - **Title Format:** *(optional)* Specify the format for the title to be displayed on the button.
    - This can include any of:
      - `{projectName}` : The project name
      - `{timer}` : The current timer value when running. Blank when not running
  - **Server Url:** *(required)* Change from the *default* URL to the API URL of your own/company instance

https://user-images.githubusercontent.com/920861/132741561-6f9f3ff0-a920-408d-8279-579840ce0a6b.mp4

## FAQ / Troubleshooting

- Why am I getting a yellow triangle when pressing the button?
  - Your API Key is likely incorrect
- Why am I not seeing the running timer on my button?
  - Make sure you haven't set a title, as this will override any other content
  - Make sure the API Key, Workspace name and optional the project and timer name
- Why does the timer always start with a negative number?
  - This can happen when your local computer time isn't in sync with the Clockify server time
  - Make sure you synchronize your clock with a time server

## Credits
~~~~
- Feel free to star this repository and follow me on [Twitter](https://twitter.com/DarkCisum)
- Thanks to [Bar Raiders](https://barraider.com/) for the great tooling and community
- Shout-out to [Hugh Macdonald](https://github.com/HughMacdonald) for adding the text formatting feature!
- Took some inspirations from the [Toggl plugin](https://github.com/tobimori/streamdeck-toggl)
- Using a CC0 licensed [Timer image](https://www.svgrepo.com/svg/23258/timer) for the Time Tracking category
- Talking to Clockify with the [Clockify.Net](https://github.com/Morasiu/Clockify.Net) library
- And thanks to [Clockify](https://clockify.me/) for providing an excellent time tracking tool for free

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details
