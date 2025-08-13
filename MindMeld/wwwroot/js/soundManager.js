class SoundManager {
    constructor() {
        this.sounds = {};
        this.musicEnabled = true;
        this.sfxEnabled = true;
        this.masterVolume = 0.7;
        this.musicVolume = 0.5;
        this.sfxVolume = 0.8;
        this.currentMusic = null;
        this.isAudioInitialized = false;

        // Load settings from localStorage
        this.loadSettings();

        // Preload sound effects
        this.preloadSounds();

        // Initialize audio context on first user interaction
        this.initializeAudioOnUserInteraction();
    }

    initializeAudioOnUserInteraction() {
        const handleFirstInteraction = () => {
            if (!this.isAudioInitialized) {
                this.isAudioInitialized = true;
                console.log('Audio initialized after user interaction');

                // Try to start background music if enabled
                if (this.musicEnabled) {
                    this.playSound('backgroundMusic');
                }
            }

            // Remove listeners after first interaction
            document.removeEventListener('click', handleFirstInteraction);
            document.removeEventListener('keydown', handleFirstInteraction);
            document.removeEventListener('touchstart', handleFirstInteraction);
        };

        // Listen for any user interaction
        document.addEventListener('click', handleFirstInteraction);
        document.addEventListener('keydown', handleFirstInteraction);
        document.addEventListener('touchstart', handleFirstInteraction);
    }

    preloadSounds() {
        const soundFiles = {
            click: '/sounds/click.mp3',
            success: '/sounds/success.mp3',
            fail: '/sounds/fail.mp3',
            gameStart: '/sounds/game-start.mp3',
            wordSubmit: '/sounds/word-submit.mp3',
            playerJoin: '/sounds/player-join.mp3',
            backgroundMusic: '/sounds/background-music.mp3'
        };

        Object.entries(soundFiles).forEach(([name, src]) => {
            const audio = new Audio();
            audio.src = src;
            audio.preload = 'auto';

            // Set volume based on type
            if (name === 'backgroundMusic') {
                audio.volume = this.musicVolume * this.masterVolume;
                audio.loop = true;
            } else {
                audio.volume = this.sfxVolume * this.masterVolume;
            }

            // Add error handling
            audio.addEventListener('error', (e) => {
                console.warn(`Failed to load sound: ${name}`, e);
            });

            audio.addEventListener('canplaythrough', () => {
                console.log(`Sound loaded successfully: ${name}`);
            });

            this.sounds[name] = audio;
        });
    }

    playSound(soundName) {
        const sound = this.sounds[soundName];
        if (!sound) {
            console.warn(`Sound not found: ${soundName}`);
            return Promise.reject(`Sound not found: ${soundName}`);
        }

        if (soundName === 'backgroundMusic') {
            if (!this.musicEnabled) return Promise.resolve();
            this.currentMusic = sound;
        } else {
            if (!this.sfxEnabled) return Promise.resolve();
        }

        // Reset audio to start
        sound.currentTime = 0;

        // Return promise to handle success/failure
        return sound.play().catch(e => {
            console.warn(`Sound play failed for ${soundName}:`, e.message);

            // If it's an autoplay issue, show a helpful message
            if (e.name === 'NotAllowedError') {
                console.log('Audio autoplay blocked by browser. Audio will start after user interaction.');
            }

            return Promise.reject(e);
        });
    }

    stopMusic() {
        if (this.currentMusic) {
            this.currentMusic.pause();
            this.currentMusic.currentTime = 0;
        }
    }

    toggleMusic() {
        this.musicEnabled = !this.musicEnabled;
        if (!this.musicEnabled) {
            this.stopMusic();
        } else if (this.isAudioInitialized) {
            // Only try to start music if audio is already initialized
            this.playSound('backgroundMusic');
        }
        this.saveSettings();
    }

    toggleSFX() {
        this.sfxEnabled = !this.sfxEnabled;
        this.saveSettings();
    }

    setMasterVolume(volume) {
        this.masterVolume = Math.max(0, Math.min(1, volume));
        this.updateAllVolumes();
        this.saveSettings();
    }

    setMusicVolume(volume) {
        this.musicVolume = Math.max(0, Math.min(1, volume));
        this.updateAllVolumes();
        this.saveSettings();
    }

    setSFXVolume(volume) {
        this.sfxVolume = Math.max(0, Math.min(1, volume));
        this.updateAllVolumes();
        this.saveSettings();
    }

    updateAllVolumes() {
        Object.entries(this.sounds).forEach(([name, audio]) => {
            if (name === 'backgroundMusic') {
                audio.volume = this.musicVolume * this.masterVolume;
            } else {
                audio.volume = this.sfxVolume * this.masterVolume;
            }
        });
    }

    saveSettings() {
        const settings = {
            musicEnabled: this.musicEnabled,
            sfxEnabled: this.sfxEnabled,
            masterVolume: this.masterVolume,
            musicVolume: this.musicVolume,
            sfxVolume: this.sfxVolume
        };
        localStorage.setItem('mindMeldSoundSettings', JSON.stringify(settings));
    }

    loadSettings() {
        const saved = localStorage.getItem('mindMeldSoundSettings');
        if (saved) {
            const settings = JSON.parse(saved);
            this.musicEnabled = settings.musicEnabled ?? true;
            this.sfxEnabled = settings.sfxEnabled ?? true;
            this.masterVolume = settings.masterVolume ?? 0.7;
            this.musicVolume = settings.musicVolume ?? 0.5;
            this.sfxVolume = settings.sfxVolume ?? 0.8;
        }
    }

    // Method to manually start background music (for buttons)
    startBackgroundMusic() {
        if (this.musicEnabled) {
            return this.playSound('backgroundMusic');
        }
        return Promise.resolve();
    }
}

// Global sound manager instance
const soundManager = new SoundManager();

// Global helper functions
function playClickSound() {
    return soundManager.playSound('click');
}

function playSuccessSound() {
    return soundManager.playSound('success');
}

function playFailSound() {
    return soundManager.playSound('fail');
}

function playGameStartSound() {
    return soundManager.playSound('gameStart');
}

function playWordSubmitSound() {
    return soundManager.playSound('wordSubmit');
}

function playPlayerJoinSound() {
    return soundManager.playSound('playerJoin');
}

function startBackgroundMusic() {
    return soundManager.startBackgroundMusic();
}

function stopBackgroundMusic() {
    soundManager.stopMusic();
}