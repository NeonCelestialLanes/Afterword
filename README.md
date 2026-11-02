# Afterword

Afterword is a private notebook for roleplay in Final Fantasy XIV. It records the chats you choose, keeps them on this computer, and can write a short summary only after you agree to send that one piece of text.

The journal is named Afterword. The plugin file is `RPChronicles.dll`. Open it with `/afw`. The gear in the Dalamud plugin list opens Settings.

You need to be logged into a character. Each character has their own notebook.

## The window

The side panel lists New Scene, Import Scene, My Notebook, Characters, Storylines, Stories, Scratchpad, and Settings.

**Hide** is at the bottom of that panel. It tucks the panel away and makes the window narrower, so more of the game shows. A small arrow on the left brings the panel back.

**Dark mode** is in Settings. It darkens the page. The navy cover stays.

Hide, dark mode, and Mini are remembered the next time you open Afterword.

## Mini

**Mini** sits just above Hide. It opens a small window you can leave up while you roleplay. **Full** puts the large window back, at the size it had before.

The top of the small window says Scratchpad or Scene. The other button switches between them.

**Scratchpad** in Mini is the same post as the full window. The words wrap, **Clear** empties it, spelling sits under the box, and **Copy** still puts the next piece on the clipboard.

**Scene** in Mini does not keep its own list of people or its own place. It uses what you already chose on **New Scene** in the full window.

- Type a title, then press **Start recording**.
- The line under the title says who will be recorded. That is everyone, the names you picked, or nobody.
- If nobody is selected, the scene stays quiet until you pick someone in the full window.
- Change the people on **New Scene**, or on **Who is in this scene** while a scene is going, and Mini follows.
- Lines show up as people talk.
- **Pause** stops new lines. **Resume** continues. **Stop** saves the scene into My Notebook.

## The link to put in Dalamud

Put this link in **Dalamud Settings**, on the **Experimental** tab, in **Custom Plugin Repositories**:

https://raw.githubusercontent.com/NeonCelestialLanes/Afterword/main/pluginmaster.json

That is the link Dalamud reads. Afterword then shows up in the plugin installer.

## Record a scene

1. Open **New Scene**.
2. Give it a title.
3. Set the place. It starts as where you are. You can type something else, such as a letter, a dream, or an off-screen place.
4. Choose **everyone** nearby, or pick the people whose lines you want.
5. Press **Start**. Nothing from before Start is kept.

**Pause** holds the scene and stops new lines. **Stop and save** puts it in My Notebook.

While a scene is recording, what people say shows up under **Lines**. You can still change who is included. Mini uses that same choice.

Say and emote are on by default. Shout, yell, tell, party, alliance, free company, linkshells, and Novice Network stay off until you turn them on in Settings.

**Random rolls** is in Settings with the other chats. It is off until you turn it on. A `/random` is then saved as that person rolling a number out of a maximum. Loot rolls, combat, echoes, and other system text stay out.

Out-of-character text is skipped when that setting is on. Text wrapped in `[ ]`, `( )`, or `(( ))` is removed. A line that only starts or ends with one of those marks is left out, and the rest of a mixed line is kept.

A tell prefix `>>` on a name is ignored, so `>> Lucien Vaelmont` is the same person as `Lucien Vaelmont`.

## Saved scenes

Open a scene from **My Notebook**.

- Edit a speaker or a line. **Delete line** asks you to press it again.
- **New scene starts at** takes a clock time, such as `10:45 AM`. Lines from that time on become a new scene. The scene you are in keeps the earlier lines. **New scene** on a line does the same from that line.
- **Combine with another log** moves that log's lines into the one you have open, in the order they were said. This log keeps its name. The other log is removed. It asks you to press **Combine?** before it does that.
- Attach the scene to a storyline.
- Ask for a summary, or type one yourself.

The list can be newest first or oldest first, and you can search by a word in the title, place, summary, storyline, speaker, or line.

## Import a scene

**Import Scene** reads a `.txt`, `.md`, or `.log` file on this computer. You choose who to keep before it is saved. From a storyline, **Import a scene** files that import under the storyline.

A line can start with a tag and a name:

```text
[say] Alyra Voss: The door was already open.
[emote] Corvin Hale glances over.
```

`[shout]`, `[yell]`, `[tell]`, and `[party]` are accepted too. A time in brackets, such as `[10:45 AM]`, is kept as the line's time.

## Scratchpad

**Scratchpad** is in the sidebar, under Storylines. Paste or write a post. Afterword splits it so each piece fits in the chat box, breaks on a sentence when it can, and numbers the pieces `(1/2)`, `(2/2)`, and so on.

The words wrap inside the box. A blank line starts a new paragraph.

Choose Say, Emote, or another channel. **Copy 1/2** puts that piece on the clipboard, command included, so you can paste it into chat and come back for the next one. A post that fits in one message is not numbered. **Clear** empties the post.

**Spelling** sits under the box and uses the spelling checker already on Windows. Pick a suggestion, press **Remove** for a word typed twice, or press **Keep** to leave a name alone for this session.

## Notes

**My Notebook** has notes that are not scenes. A storyline can have its own notes too. Notes stay on this computer. They are never included when you send a summary.

## Characters

Anyone who speaks in a scene is listed under **Characters**. You can also add someone who is not on screen, such as an NPC.

On a character you can:

- Rename them. Their lines are updated.
- Mark two names as the same person and combine them. Lines and notes move onto the name you have open.
- **Delete character** asks you to press it again. That name and their lines are removed.
- Write notes.
- Ask for a summary of who they are and what they seem to want. It does not retell what they did.

## Storylines

A storyline groups scenes that share a name.

- **Date** is when the story began. Type a date such as `Nov 1, 2026`. It saves once the year is clear. The list puts the newest date first. A storyline with no date stays at the bottom.
- The storyline has its own summary, notes, and scene list.

**Delete storyline** asks you to press it again. The group goes away. The scenes stay in the notebook. A long story that was linked to it stays in Stories, with the storyline cleared.

## Stories

**Stories** is for long writing, not a chat log. A story can be attached to a storyline. **This affects the lore** marks it as background that happened. When you summarize that storyline, the story is included as lore instead of as another scene.

**Delete story** asks you to press it again.

## Summaries

Settings lets you choose **ChatGPT** or **Grok**. Sign in with the plan you already pay for. There is no API key and no second bill.

Afterword asks before every scene, storyline, or character is sent. The question names the service. Only the text you confirm leaves this computer.

A scene or storyline summary is written from the lines, in two to four sentences, naming people and what those lines show. It is not written as one of the characters. A character summary is a short portrait of the person.

Signing in with ChatGPT does not read that account's chats or memory. The summary uses only the lines you agreed to send.

Sign out removes the saved sign-in from this computer.

## Where the notebook is saved

Everything for this plugin is under:

`%AppData%\XIVLauncher\pluginConfigs\RPChronicles\`

| File | What it holds |
| --- | --- |
| `notebooks\` | One notebook for each character |
| `logs\` | One text file per scene, unless Settings points somewhere else |
| `recording-settings.json` | Which chats are kept, plus dark mode and the window size |
| `summary-settings.json` | ChatGPT or Grok |
| `chatgpt-login.json` | The ChatGPT sign-in, if you used it |
| `grok-login.json` | The Grok sign-in, if you used it |

Settings has **Where logs are saved**. Choose a folder, or use the usual place. A scene with a title is saved as the date and that title, such as `2026-11-01 The Frostbite after Ishgard.txt`. A scene without a title is saved as the date and time, such as `2026-11-01 4-57 PM.txt`.

If the game closes while a scene is recording, the lines already kept are saved and the scene is paused.

## Build

This plugin targets Dalamud API 15.

```powershell
dotnet build
```

The files Dalamud needs are `bin\Debug\RPChronicles.dll` and `bin\Debug\RPChronicles.json`. In Dalamud settings, open **Dev Plugins** and add the `bin\Debug` folder. Enable Afterword in the plugin list. After a new build, reload the plugin in game.
