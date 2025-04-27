# Gemini Overlay

A lightweight desktop assistant that provides quick access to Google's Gemini API through a convenient overlay interface. Gemini Overlay stays in your system tray and can be activated with a simple keyboard shortcut.

## Demo

![Demo](Demo/showcase.gif)


## Features

- **Always Available**: Access with Ctrl+Shift+G keyboard shortcut
- **Minimal Interface**: Appears as an overlay on top of your current work
- **Image Analysis**: Capture screenshot regions to send to Gemini for analysis
- **Multiple Models**: Support for various Gemini models.
- **Markdown Support**: Rendering of responses with markdown.

## Installation

1. Download the latest release from the [Releases](https://github.com/mre31/gemini-overlay/releases) page
2. Extract the zip file to a location of your choice
3. Run `GeminiOverlay.exe`

## Setup

1. Create a `.env` file in the same directory as the executable
2. Add your Gemini API key:
   ```
   API_KEY_1=your_api_key_here
   ```
3. You can add multiple API keys to use them without hittings free limits.
   ```
   API_KEY_1=your_first_api_key
   API_KEY_2=your_second_api_key
   ```

## Usage

### Basic Usage

1. Press `Ctrl+Shift+G` to show the overlay
2. Type your question and press Enter
3. The response will appear in the overlay
4. Press `Esc` to dismiss the overlay

### Image Analysis

1. Show the overlay with `Ctrl+Shift+G`
2. Press `Ctrl+S` to capture a region of your screen
3. After selecting a region, type your question about the image
4. Press Enter to send both the image and your question to Gemini

### System Tray Options

Right-click the system tray icon to access:

- Show/Hide the overlay
- Start with Windows option
- Model selection
- Exit application

## Configuration

Settings are stored in `%AppData%\GeminiOverlay\settings.json` and include:

- Selected model preference
- Startup settings
- Hotkey configuration

## Building from Source

### Prerequisites

- Visual Studio 2019 or newer
- .NET Framework 4.8

### Build Steps

1. Clone this repository
2. Open `Gemeni.csproj` in Visual Studio
3. Build the solution
4. Create a `.env` file in the output directory with your API key

## License

This project is licensed under the GPL-3.0 License - see the [LICENSE](LICENSE) file for details. 
