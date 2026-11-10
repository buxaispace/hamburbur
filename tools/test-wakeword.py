#!/usr/bin/env python3
"""Headless Vosk grammar wake-word test. Prints pass/fail only, not transcripts."""
import json
import os
import sys
import wave

from vosk import KaldiRecognizer, Model, SetLogLevel

GRAMMAR = '["jarvis","system","assistant","friday","echo","cortana","mainframe","ultron","terminal","[unk]"]'
WAKE = {"jarvis", "system", "assistant", "friday", "echo", "cortana", "mainframe", "ultron", "terminal"}


def detect(model, path):
    wf = wave.open(path, "rb")
    rec = KaldiRecognizer(model, wf.getframerate(), GRAMMAR)
    rec.SetWords(True)
    while True:
        data = wf.readframes(4000)
        if not data:
            break
        rec.AcceptWaveform(data)
    text = (json.loads(rec.FinalResult()).get("text") or "").strip().lower()
    for part in text.split():
        if part in WAKE:
            return part
    return None


def main():
    SetLogLevel(-2)
    model = Model(os.environ.get("VOSK_MODEL_DIR", "/tmp/wakeword/vosk-model-small-en-us-0.15"))
    failed = 0
    for wav in sys.argv[1:]:
        hit = detect(model, wav)
        expect = os.path.splitext(os.path.basename(wav))[0].split("_")[0]
        ok = hit == expect if expect in WAKE else hit is None
        print(f"{os.path.basename(wav)}\texpect={expect}\thit={hit or '-'}\t{'PASS' if ok else 'FAIL'}")
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
