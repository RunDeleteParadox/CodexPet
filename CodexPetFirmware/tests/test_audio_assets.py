"""Verify approval identity, embedded bytes and supported PCM format offline."""
import hashlib
from pathlib import Path
import re
import unittest
import wave

root = Path(__file__).resolve().parents[1]

class AudioAssets(unittest.TestCase):
    def test_approved_voice_is_byte_identical(self):
        data = (root/'assets/audio/tada-voice.wav').read_bytes()
        self.assertEqual(hashlib.sha256(data).hexdigest(), '1e34abf9b697f61ea75ad3c9e99c1a8f832109188b290a1288df80175b56b938')

    def test_embedded_payloads_and_pcm_contract(self):
        source = (root/'src/success_audio_data.cpp').read_text()
        for symbol, filename in [('kSuccessVoiceWav','tada-voice.wav'), ('kSuccessFanfareWav','tada-two-notes.wav')]:
            with self.subTest(sound=filename):
                path = root/'assets/audio'/filename
                body = re.search(symbol+r'\[\] = \{([^}]+)\}',source).group(1)
                embedded = bytes(int(b,16) for b in re.findall(r'0x([0-9a-f]{2})',body))
                self.assertEqual(embedded,path.read_bytes())
                with wave.open(str(path)) as audio:
                    self.assertEqual((audio.getnchannels(),audio.getsampwidth(),audio.getcomptype()),(1,2,'NONE'))
                    self.assertIn(audio.getframerate(),[22050,24000])
                    self.assertGreater(audio.getnframes()/audio.getframerate(),0.5)
                    self.assertLess(audio.getnframes()/audio.getframerate(),1.0)

if __name__ == '__main__': unittest.main()
