// The rest countdown reached zero. With `beep`, a short beep, best effort (the app buzzes the phone itself; when the watch is connected, it buzzes instead).
export function alert(beep) {
    // Leave any field first, so a number still being typed is saved before the next set starts.
    document.activeElement?.blur();
    if (beep) {
        try {
            const audio = new AudioContext();
            const tone = audio.createOscillator();
            const volume = audio.createGain();
            tone.frequency.value = 880;
            volume.gain.setValueAtTime(0.3, audio.currentTime);
            volume.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + 0.5);
            tone.connect(volume).connect(audio.destination);
            tone.onended = () => audio.close();
            tone.start();
            tone.stop(audio.currentTime + 0.5);
        } catch {
            // No sound; the buzz still tells.
        }
    }

    // Give the left field's change a moment to reach the app.
    return new Promise(resolve => setTimeout(resolve, 50));
}
