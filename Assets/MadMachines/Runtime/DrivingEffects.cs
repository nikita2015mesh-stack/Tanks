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
        private static readonly Dictionary<Scene, DrivingEffects> Instances = new Dictionary<Scene, DrivingEffects>();
        private static AudioClip engineClip;
        private Mesh trackMesh;
        private Material trackMaterial;
        private Material particleMaterial;
        private Material tracerMaterial;
        private Material wakeMaterial;
        private ParticleSystem particles;
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
            Play(light ? Clips.Machinegun : ammunition.TimedBounce ? Clips.Grenade : Clips.Cannon,
                point, light ? 0.52f : 0.88f, light ? 65f : 150f);
            Emit(point + direction * 0.22f, direction * 2.5f, light ? Vary(0.27f, 0.46f) : 1.05f, light ? 0.045f : 0.075f,
                new Color(1f, 0.91f, 0.63f, 1f));

            if (ammunition.TimedBounce)
            {
                for (int i = 0; i < 6; i++)
                {
                    bool spark = i == 0;
                    Vector3 velocity = direction * Vary(1.5f, 4f) + Drift();
                    Emit(point + direction * 0.12f, spark ? velocity * 1.8f : velocity,
                        Vary(0.15f, 0.34f) * 1.6f, spark ? 0.10f : Vary(0.16f, 0.45f),
                        spark ? new Color(1f, 0.68f, 0.22f, 0.95f) : new Color(0.63f, 0.58f, 0.52f, 0.48f));
                }
                return;
            }

            int smokeCount = light ? 1 : 9;
            for (int i = 0; i < smokeCount; i++)
            {
                Vector3 velocity = direction * Vary(0.7f, 3.2f) + Vector3.up * Vary(0.35f, 1.0f) + Drift() * 0.65f;
                float brightness = Vary(0.43f, 0.68f);
                Emit(point + direction * Vary(0.14f, 0.35f), velocity,
                    light ? Vary(0.14f, 0.29f) : Vary(0.34f, 0.76f), light ? Vary(0.15f, 0.32f) : Vary(0.45f, 1.05f),
                    new Color(brightness, brightness * 0.98f, brightness * 0.94f, light ? 0.27f : 0.5f));
            }

            if (variation.NextDouble() < (light ? 0.12 : 0.3))
            {
                int sparkCount = light ? 1 : 3;
                for (int i = 0; i < sparkCount; i++)
                    Emit(point + direction * 0.18f, direction * Vary(5f, 10f) + Drift() * 2.5f,
                        Vary(0.045f, 0.105f), Vary(0.08f, 0.16f), new Color(1f, 0.68f, 0.22f, 0.95f));
            }
        }

        internal void Impact(ProjectileDefinition ammunition, Vector3 point)
        {
            bool blast = ammunition.BlastRadius > 0f;
            Play(blast ? Clips.Explosion : Clips.Hit, point, blast ? 0.9f : 0.48f, blast ? 170f : 60f);
            float scale = blast ? Mathf.Sqrt(ammunition.BlastRadius) : 0.55f;
            if (blast)
                Emit(point + Vector3.up * 0.2f, Vector3.up * 1.25f, scale * 1.15f, 0.13f,
                    new Color(1f, 0.83f, 0.42f, 0.98f));
            Color dustColor = new Color(0.61f, 0.49f, 0.37f, 0.62f);
            bool looseImpact = false;
            if (gameObject.scene.GetPhysicsScene().Raycast(point + Vector3.up * 0.4f, Vector3.down, out RaycastHit ground,
                blast ? 3.5f : 0.8f, 1, QueryTriggerInteraction.Ignore) && ground.collider.TryGetComponent(out LooseGround loose))
            {
                looseImpact = true;
                dustColor = WithAlpha(loose.Dust, 0.7f);
            }

            bool bulletSparks = !blast && !looseImpact && variation.NextDouble() < 0.25;
            int sparks = blast ? 4 : bulletSparks ? 2 : 0;
            for (int i = 0; i < (blast ? 18 : 4); i++)
            {
                Vector3 spread = Drift();
                spread.y = Mathf.Abs(spread.y);
                bool spark = i < sparks;
                Emit(point + spread * (0.08f * scale), (spread * 2.5f + Vector3.up * 0.45f) * scale,
                    Vary(0.27f, 0.7f) * scale, spark ? 0.16f : Vary(0.28f, 0.95f),
                    spark ? new Color(1f, 0.59f, 0.22f, 0.95f) : dustColor);
            }
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
                Emit(point + normal * 0.06f, direction, Vary(0.075f, 0.19f), Vary(0.11f, 0.29f), spark);
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
                if (tracerMaterial == null) tracerMaterial = CreateMaterial("Projectile tracer", 2f, 3000);
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
            if (particles == null) CreateParticles();
            ParticleSystem.EmitParams emission = new ParticleSystem.EmitParams
            {
                position = point,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = color,
                rotation = Vary(0f, 360f)
            };
            particles.Emit(emission, 1);
        }

        private void CreateParticles()
        {
            GameObject emitter = new GameObject("Dust and sparks") { layer = gameObject.layer };
            emitter.transform.SetParent(transform, false);
            particles = emitter.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 520;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.45f, 1f, 1.6f));
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.15f, 0f), new GradientAlphaKey(1f, 0.14f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystemRenderer renderer = emitter.GetComponent<ParticleSystemRenderer>();
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV
            });
            particleMaterial = CreateMaterial("Dust", 1f, 3001);
            renderer.sharedMaterial = particleMaterial;
            particles.Play();
        }

        private void LateUpdate()
        {
            UpdateAudioPause();
            float now = Time.time;
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

        private static Material CreateMaterial(string name, float mode, int queue)
        {
            Material material = new Material(Resources.Load<Shader>("DrivingFeedback")) { name = name, renderQueue = queue };
            material.SetFloat("_Mode", mode);
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
        }
    }
}
