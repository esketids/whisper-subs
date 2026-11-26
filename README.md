<p align="center">
  <img src="docs/images/banner.svg" alt="WhisperSubs" width="900"/>
</p>

<p align="center">
  <a href="https://github.com/esketids/whisper-subs/releases"><img src="https://img.shields.io/github/v/release/esketids/whisper-subs?style=flat-square&logo=github&color=F2B33D" alt="Release"></a>
  <a href="https://github.com/esketids/whisper-subs/actions/workflows/build-release.yml"><img src="https://img.shields.io/github/actions/workflow/status/esketids/whisper-subs/build-release.yml?branch=main&style=flat-square&label=tests" alt="Tests"></a>
  <a href="https://github.com/esketids/whisper-subs/blob/main/LICENSE"><img src="https://img.shields.io/github/license/esketids/whisper-subs?style=flat-square" alt="License"></a>
  <a href="https://github.com/esketids/whisper-subs/stargazers"><img src="https://img.shields.io/github/stars/esketids/whisper-subs?style=flat-square" alt="Stars"></a>
  <a href="https://codecov.io/gh/esketids/whisper-subs"><img src="https://img.shields.io/codecov/c/github/esketids/whisper-subs?style=flat-square" alt="Coverage"></a>
</p>

**WhisperSubs** is a Jellyfin plugin that writes subtitles for the titles in your library that have none, by transcribing their audio with local AI models. Download providers such as OpenSubtitles only serve what someone has uploaded, and Bazarr's Whisper path needs a second service running beside it; WhisperSubs runs inside Jellyfin and makes the subtitle from the audio, in the audio's own language, and can translate it into English or, from English audio, into 24 more languages. Transcription runs on hardware you control: an [akou](https://github.com/GeiserX/akou) server next to Jellyfin by default, or this Jellyfin server itself, plus any workers you add, and your media never reaches a third-party cloud unless you deliberately configure one.

<p align="center"><img src="docs/images/screenshots/playback.jpg" alt="Tears of Steel playing in Jellyfin with the subtitle track WhisperSubs made from its audio" width="880"></p>

## Features

- A title with no subtitles gets one in the language of its audio, saved next to the file as `Movie.es.WhisperSubs.srt`, and the title is refreshed at once so the player offers the track right away.
- A film in a language you do not speak gets an English subtitle too; English audio can also get subtitles in 24 European languages (Canary, experimental).
- A film with a few lines of foreign dialogue gets a forced subtitle that covers only those lines.
- A title you ask for jumps ahead of the nightly sweep. Your users can ask too, and a request costs no CPU until you approve it.
- **Generate Subtitles** and **Translate into...** sit on every item page; a live banner on the settings page shows the current title, the phase and the queue.
- The engine and a model download from the settings page on Linux (macOS and Windows servers install `whisper-cli` themselves), and a GPU is used when there is one: CUDA, Vulkan or ROCm.
- One switch swaps the transcription model for Qwen3-ASR 1.7B (experimental), run through CrispASR; detection and English translation stay on Whisper.
- Transcription can spread across your own workers or a hosted endpoint, free workers first.
- An interrupted job resumes from its last timestamp, and the daily sweep skips what it already checked.
- Music libraries can get `.lrc` lyrics (experimental), and vocal separation can clean noisy audio before transcription (optional).

## Quick start

1. Start an akou server next to Jellyfin, fetch its Qwen3-ASR model and make a key for the plugin:

   ```bash
   docker network create akou-net
   docker network connect akou-net jellyfin   # your Jellyfin container's name
   docker run -d --name akou --network akou-net -e AKOU_BEHIND_PROXY=true \
     -v akou-data:/data -v akou-models:/models drumsergio/akou:1.2.4
   docker exec akou akou models pull best
   docker exec akou akou keys create --name whisper-subs --scope jobs
   ```

   akou refuses to start on a container's open address unless `AKOU_BEHIND_PROXY=true` says something else guards it. Here nothing publishes the port, so only containers on `akou-net` reach it.

2. In Jellyfin, go to **Dashboard** > **Plugins** > **Repositories** and add `https://raw.githubusercontent.com/esketids/whisper-subs/main/manifest.json`.
3. Install **WhisperSubs** from **Catalog** and restart Jellyfin.
4. Open **Dashboard** > **Plugins** > **My Plugins** > **WhisperSubs**. Under **Connect an akou server**, enter the URL (`http://akou:8476`, the container's name on the shared network) and the `ak_` key, press **Test**, then **Save**. Press **Generate Subtitles** on any title.

A few minutes later the player offers a subtitle track named **WhisperSubs**, and `<title>.<lang>.WhisperSubs.srt` sits next to the media file. akou covers 30 languages, names the language of untagged titles and, from the akou release with its translate task, makes the English translation too. Forced subtitles and the other languages need the local Whisper engine, and so does English translation with an older akou: open **Transcribe on this server instead** on the same page to download it, or skip akou and use that engine alone, as the [setup guide](https://github.com/esketids/whisper-subs/blob/main/docs/docs/setup.md) explains; on a macOS or Windows server you install `whisper-cli` yourself and set its path. It runs on Jellyfin 10.11 and 12.1, with or without a GPU.

## Documentation

Everything past this page lives in **[github.com/esketids/whisper-subs/tree/main/docs](https://github.com/esketids/whisper-subs/tree/main/docs)**.

- [Setup guide](https://github.com/esketids/whisper-subs/blob/main/docs/docs/setup.md): the engine and model downloads, container libraries, GPU passthrough, vocal separation
- [Choosing a binary variant](https://github.com/esketids/whisper-subs/blob/main/docs/choosing-a-variant.md): which of the seven builds your CPU and GPU can run, exit code 132
- [Platform-specific installs](https://github.com/esketids/whisper-subs/blob/main/docs/install-topologies.md): TrueNAS SCALE, Proxmox LXC, Synology DSM, unRAID, Docker Compose
- [Settings reference](https://github.com/esketids/whisper-subs/blob/main/docs/configuration.md): every setting, what it does, its default
- [Remote workers](https://github.com/esketids/whisper-subs/blob/main/docs/remote-workers.md): the worker pool, the worker container, Groq, OpenAI or OpenRouter as a backend
- [Diagnostics](https://github.com/esketids/whisper-subs/blob/main/docs/diagnostics.md): the status and queue endpoints, API keys, what a useful bug report contains
- [Limitations](https://github.com/esketids/whisper-subs/blob/main/docs/limitations.md): what it cannot do, and why
- [Features in detail](https://github.com/esketids/whisper-subs/blob/main/docs/features.md): output files, subtitle modes, user requests, models, the in-page button
- [API and internals](https://github.com/esketids/whisper-subs/blob/main/docs/api-and-internals.md): the REST endpoints and how a subtitle gets made

Planned work is in [ROADMAP.md](ROADMAP.md).

## License

[GPL-3.0-or-later](LICENSE). Forked from upstream WhisperSubs by `esketids`.
