using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Gait audio for the horse. Our custom HAP clips have no vanilla "footstep" animation curve, so vanilla's FootStep
    /// component can't fire — instead we play the user's cleanly-looping gait WAVs (walk/trot/run) on a spatial
    /// AudioSource attached to the horse, selected + shaped by the (already normalized) forward_speed we drive.
    ///
    /// The Unity AudioClip/AudioSource API is used by REFLECTION on purpose: a direct compile-time reference to
    /// UnityEngine.AudioModule drags in netstandard 2.1 and breaks the net48 build (see reference_audio_reflection_net48 /
    /// FiresCore.UI.PencilSfx). Clips load from BepInEx/config/FiresMountSounds/&lt;walk|trot|run&gt;.wav (16-bit PCM) so
    /// they can be swapped without a rebuild.
    /// </summary>
    internal static class MountAudio
    {
        private static bool _init, _failed;
        private static Type _tClip, _tSource;
        private static MethodInfo _clipCreate, _clipSetData;
        private static readonly Dictionary<string, object> _clips = new Dictionary<string, object>();   // name -> AudioClip (null = tried+absent)

        internal static Type SourceType => _tSource;
        internal static bool Ready => _init && !_failed && _tClip != null && _tSource != null;

        internal static void EnsureInit()
        {
            if (_init) return;
            _init = true;
            try
            {
                _tClip = Type.GetType("UnityEngine.AudioClip, UnityEngine.AudioModule") ?? Type.GetType("UnityEngine.AudioClip, UnityEngine");
                _tSource = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule") ?? Type.GetType("UnityEngine.AudioSource, UnityEngine");
                if (_tClip == null || _tSource == null) { _failed = true; return; }
                _clipCreate = _tClip.GetMethod("Create", new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) });
                _clipSetData = _tClip.GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
                if (_clipCreate == null || _clipSetData == null) _failed = true;
            }
            catch (Exception ex) { _failed = true; try { FiresMountsPlugin.Log.LogWarning($"[FiresMounts] audio init failed: {ex.Message}"); } catch { } }
        }

        /// <summary>Cached AudioClip for gait short-name (walk/trot/run), loaded from config/FiresMountSounds/&lt;name&gt;.wav.</summary>
        internal static object GetClip(string name)
        {
            EnsureInit();
            if (!Ready) return null;
            if (_clips.TryGetValue(name, out var cached)) return cached;
            object clip = null;
            try
            {
                string path = Path.Combine(Paths.ConfigPath, "FiresMountSounds", name + ".wav");
                if (File.Exists(path) && WavMini.TryLoad(path, out float[] buf, out int ch, out int sr))
                {
                    ch = Mathf.Max(1, ch);
                    clip = _clipCreate.Invoke(null, new object[] { "firesmount_" + name, buf.Length / ch, ch, sr, false });
                    _clipSetData.Invoke(clip, new object[] { buf, 0 });
                }
            }
            catch (Exception ex) { try { FiresMountsPlugin.Log.LogWarning($"[FiresMounts] load {name}.wav failed: {ex.Message}"); } catch { } }
            _clips[name] = clip;   // cache hit or miss so we parse each WAV at most once
            return clip;
        }
    }

    /// <summary>A looping, spatial AudioSource (one per horse) driven through reflection so no AudioModule type is referenced.</summary>
    internal sealed class MountLoopSource
    {
        private static PropertyInfo _pPlayOnAwake, _pLoop, _pSpatial, _pVolume, _pPitch, _pClip, _pMinDist, _pMaxDist, _pDoppler, _pBypassReverb;
        private static MethodInfo _mPlay, _mStop;
        private static bool _ready, _failed;

        private readonly Component _src;
        private object _curClip;
        private bool _playing;

        private static void EnsureMembers(Type t)
        {
            if (_ready || _failed) return;
            try
            {
                _pPlayOnAwake = t.GetProperty("playOnAwake"); _pLoop = t.GetProperty("loop");
                _pSpatial = t.GetProperty("spatialBlend"); _pVolume = t.GetProperty("volume");
                _pPitch = t.GetProperty("pitch"); _pClip = t.GetProperty("clip");
                _pMinDist = t.GetProperty("minDistance"); _pMaxDist = t.GetProperty("maxDistance");
                _pDoppler = t.GetProperty("dopplerLevel");
                _pBypassReverb = t.GetProperty("bypassReverbZones");
                _mPlay = t.GetMethod("Play", Type.EmptyTypes); _mStop = t.GetMethod("Stop", Type.EmptyTypes);
                _ready = _mPlay != null && _mStop != null && _pClip != null && _pVolume != null;
                if (!_ready) _failed = true;
            }
            catch { _failed = true; }
        }

        public static MountLoopSource Attach(GameObject host)
        {
            MountAudio.EnsureInit();
            if (!MountAudio.Ready || host == null) return null;
            EnsureMembers(MountAudio.SourceType);
            if (!_ready) return null;
            try
            {
                var src = host.AddComponent(MountAudio.SourceType);
                _pPlayOnAwake?.SetValue(src, false);
                _pLoop?.SetValue(src, true);
                _pSpatial?.SetValue(src, 1f);      // 3D so it positions on the horse
                _pDoppler?.SetValue(src, 0f);      // no pitch warble from the horse's own motion
                _pBypassReverb?.SetValue(src, true);   // gait loops shouldn't pick up building/cave reverb zones (the "tunnel" echo)
                _pMinDist?.SetValue(src, 8f);      // full volume out to 8m so the 3rd-person camera (sits back a few m) hears it at full
                _pMaxDist?.SetValue(src, 48f);
                _pVolume?.SetValue(src, 0f);
                return new MountLoopSource(src);
            }
            catch { return null; }
        }

        private MountLoopSource(Component src) { _src = src; }

        public void SetClip(object clip)
        {
            if (_src == null || clip == null || ReferenceEquals(clip, _curClip)) return;
            try { _pClip.SetValue(_src, clip); _curClip = clip; if (_playing) _mPlay.Invoke(_src, null); } catch { }
        }
        public void SetVolume(float v) { if (_src != null) try { _pVolume.SetValue(_src, Mathf.Clamp01(v)); } catch { } }
        public void SetPitch(float p) { if (_src != null && _pPitch != null) try { _pPitch.SetValue(_src, Mathf.Clamp(p, 0.5f, 2f)); } catch { } }
        public void Play() { if (_src == null || _playing || _curClip == null) return; try { _mPlay.Invoke(_src, null); _playing = true; } catch { } }
        public void Stop() { if (_src == null || !_playing) return; try { _mStop.Invoke(_src, null); _playing = false; } catch { } }
    }

    /// <summary>Minimal 16-bit PCM WAV parser → normalized float samples + format (mono or interleaved). AudioClip built by caller.</summary>
    internal static class WavMini
    {
        public static bool TryLoad(string path, out float[] samples, out int channels, out int sampleRate)
        {
            samples = null; channels = 0; sampleRate = 0;
            try
            {
                byte[] d = File.ReadAllBytes(path);
                if (d.Length < 44 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F') return false;
                channels = BitConverter.ToInt16(d, 22);
                sampleRate = BitConverter.ToInt32(d, 24);
                int bits = BitConverter.ToInt16(d, 34);
                if (bits != 16 || channels < 1) return false;
                int p = 12, dataOff = -1, dataLen = 0;
                while (p + 8 <= d.Length)
                {
                    int len = BitConverter.ToInt32(d, p + 4);
                    if (d[p] == 'd' && d[p + 1] == 'a' && d[p + 2] == 't' && d[p + 3] == 'a') { dataOff = p + 8; dataLen = len; break; }
                    p += 8 + len + (len & 1);
                }
                if (dataOff < 0) return false;
                dataLen = Math.Min(dataLen, d.Length - dataOff);
                int total = dataLen / 2;
                samples = new float[total];
                for (int i = 0; i < total; i++) samples[i] = BitConverter.ToInt16(d, dataOff + i * 2) / 32768f;
                return true;
            }
            catch { return false; }
        }
    }
}
