# Success sounds

- `tada-voice.wav` : original mascot voice source, 0.840 s, mono PCM16, 24 kHz. SHA-256 `1e34abf9b697f61ea75ad3c9e99c1a8f832109188b290a1288df80175b56b938`.
- `tada-two-notes.wav` : locally synthesized two-note cue, 0.570 s, mono PCM16, 22.05 kHz. SHA-256 `d06b9cd5c58d28462e6105068440f184c838a415697edc87fe188a0c3dc2c573`.

The voice was generated with the official [Qwen3-TTS VoiceDesign demo](https://huggingface.co/spaces/Qwen/Qwen3-TTS), using the text “Ta-da!” and an original tiny, friendly, mischievous creature voice description: happy and proud, soft consonants and attack, rounded vowels, a slight rasp, no shouting. No person's reference recording was used. Only outer silence trimming, gain normalization and 4 ms edge fades were applied; no pitch or duration transformation. Both cues peak at approximately -9.5 dBFS.

`python scripts/embed_success_audio.py` generates `src/success_audio_data.cpp` verbatim. The static, flash-backed WAVs remain alive throughout asynchronous M5Unified playback. No filesystem, network, speech model or codec library is required on the Pet.

Only `REACT success` starts a cue. The Hub selects `SOUND none|fanfare|voice`; the boot default is silent until configured. Other expressions, wake and base restoration never trigger audio. Volume is initially 128/255 in M5Unified and must be judged on the actual speaker.
