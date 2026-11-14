using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    internal sealed class DrivingEffects : MonoBehaviour
    {
        internal const float TreadRepeat = 0.46f;
        private const int TrackLimit = 2048;
        private const float TrackLifetime = 3.2f;
        private const int VoiceLimit = 16;
        private const int FlashLimit = 8;
        private static readonly Dictionary<Scene, DrivingEffects> Instances = new Dictionary<Scene, DrivingEffects>();
        private static AudioClip engineClip;
        private Mesh trackMesh;
        private Material trackMaterial;
        private Material particleMaterial;
        private Material tracerMaterial;
        private Material wakeMaterial;
        private Material flashMaterial;
        private Material fireMaterial;
        private ParticleSystem particles;
        private ParticleSystem fireParticles;
        private readonly MuzzleFlash[] flashes = new MuzzleFlash[FlashLimit];
        private int nextFlash;
        private Light effectLight;
        private float lightBirth;
        private float lightDuration;
        private float lightPeak;
        private readonly Queue<TrackSegment> tracks = new Queue<TrackSegment>(TrackLimit);
        private readonly List<Vector3> vertices = new List<Vector3>(TrackLimit * 4);
        private readonly List<Color32> colors = new List<Color32>(TrackLimit * 4);
        private readonly List<Vector2> uv = new List<Vector2>(TrackLimit * 4);
        private readonly List<Vector2> ages = new List<Vector2>(TrackLimit * 4);
        private readonly List<int> indices = new List<int>(TrackLimit * 6);
        private readonly AudioSource[] voices = new AudioSource[VoiceLimit];
        private readonly System.Random variation = new System.Random();
        private int nextVoice;
        private bool tracksChanged;
        private bool audioPaused;

        private struct TrackSegment
        {
            public Vector3 Start;
            public Vector3 StartAcross;
            public Vector3 End;
            public Vector3 EndAcross;
            public float From;
            public float To;
            public Color32 Color;
            public float Birth;
        }

        private struct MuzzleFlash
        {
            public LineRenderer Line;
            public float Birth;
            public float Duration;
        }

        internal static AudioClip EngineClip => engineClip != null ? engineClip : engineClip = LoadClip("Engine");

        internal static DrivingEffects In(Scene scene)
        {
            if (Instances.TryGetValue(scene, out DrivingEffects existing) && existing != null) return existing;
            GameObject root = new GameObject("Driving effects") { layer = 2 };
            SceneManager.MoveGameObjectToScene(root, scene);
            DrivingEffects effects = root.AddComponent<DrivingEffects>();
            Instances[scene] = effects;
            return effects;
        }

        internal static void ConfigureSource(AudioSource source, float range)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = range;
        }

        internal void AddTrack(Vector3 start, Vector3 startAcross, Vector3 end, Vector3 endAcross,
            float offset, float distance, Color color)
        {
            if (tracks.Count == TrackLimit) tracks.Dequeue();
            tracks.Enqueue(new TrackSegment
            {
                Start = start,
                StartAcross = startAcross,
                End = end,
                EndAcross = endAcross,
                From = offset / TreadRepeat,
                To = (offset + distance) / TreadRepeat,
                Color = color,
                Birth = Time.time
            });
            tracksChanged = true;
        }

        internal void WheelDust(Vector3 point, Vector3 normal, Vector3 movement, Color color, float intensity)
        {
            for (int i = 0; i < (intensity > 0.65f ? 2 : 1); i++)
            {
                float size = Vary(0.32f, 0.65f) + intensity * 0.62f;
                Emit(point + normal * 0.16f - movement.normalized * 0.12f,
                    movement * 0.13f + normal * Vary(0.45f, 0.95f) + Drift() * 0.45f,
                    size, Vary(0.54f, 0.88f), WithAlpha(color, Mathf.Lerp(0.2f, 0.56f, intensity)));
            }
        }

        internal void Muzzle(ProjectileDefinition ammunition, Vector3 point, Vector3 direction)
        {
            bool light = ammunition.BlastRadius <= 0f && !ammunition.IsShell;
            bool grenade = ammunition.TimedBounce;
            Play(light ? Clips.Machinegun : ammunition.TimedBounce ? Clips.Grenade : Clips.Cannon,
                point, light ? 0.52f : 0.88f, light ? 65f : 150f);
            if (!grenade)
                FlashMuzzle(point, direction, light ? Vary(0.5f, 0.85f) : Vary(1.5f, 2.1f),
                    light ? 0.25f : 0.65f, light ? 0.040f : 0.061f);
            if (!light && !grenade) PulseLight(point + direction * 0.45f, 7f, 5.5f, 0.08f);

            int smokeCount = light ? 2 : 14;
            for (int i = 0; i < smokeCount; i++)
            {
                float speed = light ? Vary(1.4f, 3.1f) : Vary(0.55f, 4.1f);
                Vector3 velocity = direction * speed + Vector3.up * Vary(0.25f, 0.8f) + Drift() * (light ? 0.29f : 0.86f);
                float shade = Vary(0.25f, 0.61f);
                Emit(point + direction * Vary(0.06f, 0.41f), velocity,
                    light ? Vary(0.15f, 0.33f) : Vary(0.38f, 1.03f), light ? Vary(0.16f, 0.39f) : Vary(0.62f, 1.44f),
                    new Color(shade, shade * 0.98f, shade * 0.94f, light ? 0.24f : 0.48f));
            }

            if (variation.NextDouble() < (light ? 0.12 : 0.3))
            {
                int sparkCount = light ? 1 : 3;
                for (int i = 0; i < sparkCount; i++)
                    EmitFire(point + direction * 0.18f, direction * Vary(6f, 12f) + Drift() * 2.5f,
                        Vary(0.035f, 0.08f), Vary(0.08f, 0.16f), new Color(1f, 0.54f, 0.17f, 0.95f));
            }
        }

        private void FlashMuzzle(Vector3 point, Vector3 direction, float length, float width, float duration)
        {
            ref MuzzleFlash flash = ref flashes[nextFlash];
            nextFlash = (nextFlash + 1) % flashes.Length;
            if (flash.Line == null)
            {
                GameObject effect = new GameObject("Muzzle flash") { layer = gameObject.layer };
                effect.transform.SetParent(transform, false);
                LineRenderer line = effect.AddComponent<LineRenderer>();
                if (flashMaterial == null) flashMaterial = CreateMaterial("Muzzle flame", 4f, 3003, additive: true);
                line.sharedMaterial = flashMaterial;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.textureMode = LineTextureMode.Stretch;
                flash.Line = line;
            }
            flash.Birth = Time.time;
            flash.Duration = duration;
            flash.Line.widthMultiplier = width;
            flash.Line.startColor = flash.Line.endColor = new Color(1f, 0.49f, 0.12f, 1f);
            flash.Line.SetPosition(0, point + direction * length);
            flash.Line.SetPosition(1, point);
            flash.Line.enabled = true;
        }

        internal void Impact(ProjectileDefinition ammunition, Vector3 point)
        {
            bool blast = ammunition.BlastRadius > 0f;
            Play(blast ? Clips.Explosion : Clips.Hit, point, blast ? 0.9f : 0.48f, blast ? 170f : 60f);
            float scale = blast ? Mathf.Sqrt(ammunition.BlastRadius) : 0.55f;
            Color dustColor = new Color(0.61f, 0.49f, 0.37f, 0.62f);
            bool nearSurface = gameObject.scene.GetPhysicsScene().Raycast(point + Vector3.up * 0.4f,
                Vector3.down, out RaycastHit ground, blast ? 3.5f : 0.8f, 1, QueryTriggerInteraction.Ignore);
            LooseGround looseGround = nearSurface ? ground.collider.GetComponent<LooseGround>() : null;
            bool looseImpact = looseGround != null;
            if (looseImpact)
            {
                dustColor = WithAlpha(looseGround.Dust, 0.7f);
            }

            if (blast)
            {
                Blast(point, scale, dustColor, nearSurface ? ground.point : (Vector3?)null, looseImpact);
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                Vector3 spread = Drift();
                spread.y = Mathf.Abs(spread.y);
                Emit(point + spread * (0.08f * scale), (spread * 2.5f + Vector3.up * 0.45f) * scale,
                    Vary(0.27f, 0.7f) * scale, Vary(0.22f, 0.52f), dustColor);
            }
            if (!looseImpact && variation.NextDouble() < 0.25)
                EmitFire(point, (Vector3.up + Drift()) * 3f, 0.085f, 0.13f, new Color(1f, 0.59f, 0.22f, 0.95f));
        }

        private void Blast(Vector3 point, float scale, Color dustColor, Vector3? surfacePoint, bool looseImpact)
        {
            PulseLight(point + Vector3.up * (scale * 0.35f), scale * 5f, scale * 6f, 0.16f);
            for (int i = 0; i < 9; i++)
            {
                Vector3 spread = Drift();
                spread.y = Mathf.Abs(spread.y);
                EmitFire(point + spread * (scale * 0.33f) + Vector3.up * 0.12f,
                    (spread * 2.1f + Vector3.up * 0.7f) * scale, Vary(0.31f, 0.89f) * scale,
                    Vary(0.08f, 0.23f), new Color(1f, Vary(0.37f, 0.76f), 0.11f, Vary(0.64f, 1f)));
            }

            for (int i = 0; i < 14; i++)
            {
                Vector3 spread = Drift();
                spread.y = Vary(0.30f, 1.25f);
                float shade = Vary(0.18f, 0.34f);
                Emit(point + spread * (scale * 0.21f), spread * scale * Vary(0.47f, 1.54f),
                    Vary(0.48f, 1.03f) * scale, Vary(0.83f, 1.9f), new Color(shade, shade * 0.93f, shade * 0.88f, 0.67f));
            }

            if (surfacePoint.HasValue)
            {
                for (int i = 0; i < (looseImpact ? 18 : 8); i++)
                {
                    float angle = Vary(0f, 2f * Mathf.PI);
                    Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    float speed = Vary(1.3f, 3.7f) * scale;
                    Emit(surfacePoint.Value + outward * (scale * Vary(0.1f, 0.54f)) + Vector3.up * 0.17f,
                        outward * speed + Vector3.up * Vary(0.11f, 0.8f), Vary(0.34f, 0.91f) * scale,
                        Vary(0.47f, 1.18f), dustColor);
                }
            }

            for (int i = 0; i < 12; i++)
            {
                Vector3 spread = Drift();
                spread.y = Mathf.Abs(spread.y) + 0.12f;
                EmitFire(point + Vector3.up * 0.25f, spread * Vary(3f, 8f) * scale, Vary(0.03f, 0.085f) * scale,
                    Vary(0.13f, 0.38f), new Color(1f, 0.51f, 0.11f, 0.82f));
            }
        }

        private void PulseLight(Vector3 point, float range, float intensity, float duration)
        {
            if (effectLight != null && effectLight.enabled && lightPeak > intensity && Time.time < lightBirth + lightDuration)
                return;
            if (effectLight == null)
            {
                GameObject lightObject = new GameObject("Shot light") { layer = gameObject.layer };
                lightObject.transform.SetParent(transform, false);
                effectLight = lightObject.AddComponent<Light>();
                effectLight.type = LightType.Point;
                effectLight.color = new Color(1f, 0.57f, 0.27f);
                effectLight.shadows = LightShadows.None;
            }
            effectLight.transform.position = point;
            effectLight.range = range;
            effectLight.intensity = intensity;
            effectLight.enabled = true;
            lightBirth = Time.time;
            lightDuration = duration;
            lightPeak = intensity;
        }

        internal void PlateImpact(Vector3 point, Vector3 normal, ArmorImpact result)
        {
            bool penetrated = result == ArmorImpact.Penetrated;
            bool reflected = result == ArmorImpact.Ricochet;
            Play(Clips.Hit, point, reflected ? 0.8f : 0.65f, 85f);
            Color spark = penetrated ? new Color(1f, 0.91f, 0.68f, 1f) : new Color(1f, 0.62f, 0.25f, 1f);
            for (int i = 0; i < (penetrated || reflected ? 12 : 6); i++)
            {
                Vector3 direction = (penetrated ? -normal : normal) * Vary(2f, 8f) + Drift() * 3f;
                EmitFire(point + normal * 0.06f, direction, Vary(0.042f, 0.11f), Vary(0.11f, 0.29f), spark);
            }
        }

        internal void ChassisImpact(Vector3 point, float strength)
        {
            Play(Clips.Crash, point, Mathf.Lerp(0.32f, 0.82f, strength), 85f);
        }

        internal Material TracerMaterial
        {
            get
            {
                if (tracerMaterial == null) tracerMaterial = CreateMaterial("Projectile tracer", 2f, 3003, additive: true);
                return tracerMaterial;
            }
        }

        internal Material WakeMaterial
        {
            get
            {
                if (wakeMaterial == null) wakeMaterial = CreateMaterial("Cannon wake", 3f, 3000);
                return wakeMaterial;
            }
        }

        private static Color WithAlpha(Color color, float opacity)
        {
            color.a *= opacity;
            return color;
        }

        private float Vary(float min, float max) => Mathf.Lerp(min, max, (float)variation.NextDouble());

        private Vector3 Drift() => new Vector3(Vary(-1f, 1f), Vary(-1f, 1f), Vary(-1f, 1f));

        private void Emit(Vector3 point, Vector3 velocity, float size, float lifetime, Color color)
        {
            if (particles == null)
            {
                particleMaterial = CreateMaterial("Dust and smoke", 1f, 3001);
                particles = CreateParticles("Dust and smoke", particleMaterial, 750, false);
            }
            EmitParticle(particles, point, velocity, size, lifetime, color);
        }

        private void EmitFire(Vector3 point, Vector3 velocity, float size, float lifetime, Color color)
        {
            if (fireParticles == null)
            {
                fireMaterial = CreateMaterial("Flame and sparks", 5f, 3002, additive: true);
                fireParticles = CreateParticles("Flame and sparks", fireMaterial, 320, true);
            }
            EmitParticle(fireParticles, point, velocity, size, lifetime, color);
        }

        private void EmitParticle(ParticleSystem system, Vector3 point, Vector3 velocity, float size, float lifetime, Color color)
        {
            ParticleSystem.EmitParams emission = new ParticleSystem.EmitParams
            {
                position = point,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = color,
                rotation = Vary(0f, 360f)
            };
            system.Emit(emission, 1);
        }

        private ParticleSystem CreateParticles(string name, Material material, int limit, bool hot)
        {
            GameObject emitter = new GameObject(name) { layer = gameObject.layer };
            emitter.transform.SetParent(transform, false);
            ParticleSystem system = emitter.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = limit;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, hot ? 1f : 0.45f, 1f, hot ? 1.35f : 1.8f));
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(hot ? 1f : 0.15f, 0f), new GradientAlphaKey(1f, 0.14f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystemRenderer renderer = emitter.GetComponent<ParticleSystemRenderer>();
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV
            });
            renderer.sharedMaterial = material;
            system.Play();
            return system;
        }

        private void LateUpdate()
        {
            UpdateAudioPause();
            float now = Time.time;
            for (int i = 0; i < flashes.Length; i++)
            {
                ref MuzzleFlash flash = ref flashes[i];
                if (flash.Line == null || !flash.Line.enabled) continue;
                float remaining = 1f - (now - flash.Birth) / flash.Duration;
                if (remaining <= 0f) flash.Line.enabled = false;
                else flash.Line.startColor = flash.Line.endColor = new Color(1f, 0.49f, 0.12f, remaining * remaining);
            }
            if (effectLight != null && effectLight.enabled)
            {
                float remaining = 1f - (now - lightBirth) / lightDuration;
                effectLight.enabled = remaining > 0f;
                effectLight.intensity = lightPeak * Mathf.Pow(Mathf.Max(0f, remaining), 2f);
            }
            while (tracks.Count > 0 && now - tracks.Peek().Birth >= TrackLifetime)
            {
                tracks.Dequeue();
                tracksChanged = true;
            }
            if (tracksChanged) UpdateTrackMesh();
            if (trackMaterial != null) trackMaterial.SetFloat("_TrackTime", now);
        }

        private void UpdateAudioPause()
        {
            bool paused = Time.timeScale == 0f;
            if (audioPaused == paused) return;
            audioPaused = paused;
            foreach (AudioSource voice in voices)
            {
                if (voice == null) continue;
                if (paused) voice.Pause();
                else voice.UnPause();
            }
        }

        private void UpdateTrackMesh()
        {
            tracksChanged = false;
            if (trackMesh == null)
            {
                trackMesh = new Mesh { name = "Tire tracks" };
                trackMesh.MarkDynamic();
                gameObject.AddComponent<MeshFilter>().sharedMesh = trackMesh;
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                trackMaterial = CreateMaterial("Tire marks", 0f, 2990);
                trackMaterial.SetFloat("_TrackLifetime", TrackLifetime);
                renderer.sharedMaterial = trackMaterial;
            }

            trackMesh.Clear();
            if (tracks.Count == 0) return;
            vertices.Clear();
            colors.Clear();
            uv.Clear();
            ages.Clear();
            indices.Clear();
            foreach (TrackSegment track in tracks)
            {
                int first = vertices.Count;
                AddTrackVertex(track.Start - track.StartAcross, 0f, track.From, track);
                AddTrackVertex(track.Start + track.StartAcross, 1f, track.From, track);
                AddTrackVertex(track.End - track.EndAcross, 0f, track.To, track);
                AddTrackVertex(track.End + track.EndAcross, 1f, track.To, track);
                indices.Add(first);
                indices.Add(first + 2);
                indices.Add(first + 1);
                indices.Add(first + 1);
                indices.Add(first + 2);
                indices.Add(first + 3);
            }
            trackMesh.SetVertices(vertices);
            trackMesh.SetColors(colors);
            trackMesh.SetUVs(0, uv);
            trackMesh.SetUVs(1, ages);
            trackMesh.SetTriangles(indices, 0);
        }

        private void AddTrackVertex(Vector3 point, float across, float along, TrackSegment track)
        {
            vertices.Add(point);
            colors.Add(track.Color);
            uv.Add(new Vector2(across, along));
            ages.Add(new Vector2(track.Birth, 0f));
        }

        private static Material CreateMaterial(string name, float mode, int queue, bool additive = false)
        {
            Material material = new Material(Resources.Load<Shader>("DrivingFeedback")) { name = name, renderQueue = queue };
            material.SetFloat("_Mode", mode);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            return material;
        }

        private void Play(AudioClip clip, Vector3 point, float volume, float range)
        {
            if (clip == null) return;
            int index = nextVoice;
            nextVoice = (nextVoice + 1) % voices.Length;
            AudioSource voice = voices[index];
            if (voice == null)
            {
                GameObject speaker = new GameObject("Impact and weapon audio");
                speaker.transform.SetParent(transform, false);
                voice = voices[index] = speaker.AddComponent<AudioSource>();
                ConfigureSource(voice, range);
            }
            voice.Stop();
            voice.transform.position = point;
            voice.maxDistance = range;
            voice.pitch = Vary(0.94f, 1.06f);
            voice.clip = clip;
            voice.volume = volume;
            voice.Play();
        }

        private static AudioClip LoadClip(string name) => Resources.Load<AudioClip>("DrivingAudio/" + name);

        private static class Clips
        {
            internal static readonly AudioClip Machinegun = LoadClip("Machinegun");
            internal static readonly AudioClip Cannon = LoadClip("Cannon");
            internal static readonly AudioClip Grenade = LoadClip("Grenade");
            internal static readonly AudioClip Hit = LoadClip("Hit");
            internal static readonly AudioClip Explosion = LoadClip("Explosion");
            internal static readonly AudioClip Crash = LoadClip("Crash");
        }

        private void OnDestroy()
        {
            Instances.Remove(gameObject.scene);
            Destroy(trackMesh);
            Destroy(trackMaterial);
            Destroy(particleMaterial);
            Destroy(tracerMaterial);
            Destroy(wakeMaterial);
            Destroy(flashMaterial);
            Destroy(fireMaterial);
        }
    }
}
