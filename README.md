# Immich Wallpaper Changer

A functional Windows desktop application that synchronizes your Immich photo library with your desktop wallpaper. 

## Functional Features

*   **Immich Integration**: Connect directly to your self-hosted Immich server.
*   **Wallpaper Filtering**:
    *   **People Mode**: Feature specific recognized faces on your desktop.
    *   **Album Mode**: Cycle through photos from a specific Immich album.
    *   **Random Mode**: Shuffle your entire library.
*   **Automation**: Automatically rotates wallpapers at user-defined intervals (ranging from minutes to hours).
*   **Background Service**: Runs in the system tray with an optional minimize-to-tray feature to keep the service active.
*   **Smart Fallback**: Supports both a Primary and Fallback Server URL to ensure connectivity whether you are on your local network or remote.
*   **Manual Adjustment**: Built-in tool to crop and rotate photos before applying them.
*   **Google Photos**: Support for Google Photos is currently in development and marked as "Coming Soon".

## Setup Instructions

### 1. Prerequisites
*   **Windows 10 or 11**
*   **.NET 8.0 Desktop Runtime**

### 2. Immich Configuration
To connect the application, you will need the following from your Immich instance:

*   **Server URL**: The address you use to access Immich (e.g., `http://192.168.1.50:2283`).
*   **Fallback URL (Optional)**: A secondary address used if the primary is unreachable (e.g., a Tailscale IP or public domain).
*   **API Key**: 
    1. Log in to your Immich web interface.
    2. Click on **User Settings** (your profile icon).
    3. Navigate to the **API Keys** section.
    4. Click **Create Key**, give it a name (e.g., "Wallpaper App"), and copy the generated secret.

### 3. Application Setup
1. Run `NasaWallpaperApp.exe`.
2. Open **Settings** from the main window or tray icon.
3. Enter your Immich Server URL and API Key.
4. Click **Verify Immich** to load your people and albums.
5. Select your desired source and enable **Auto-Refresh** if you want automatic changes.

## Troubleshooting

*   **Connection Error**: Verify that your API key hasn't expired and that your Server URL includes the port (usually 2283).
*   **Wallpaper Not Updating**: Check the system tray to ensure the application is running. Ensure "Minimize to Tray on Close" is enabled if you want it to run after closing the window.

## License
This project is licensed under the MIT License.
