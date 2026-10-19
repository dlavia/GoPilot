using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media.Audio;
using Windows.Media.Render;
using Windows.Storage;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Sonidos de navegación (foco, pulsar, abrir, cerrar, atrás) como los de la app de Xbox: scripts\make-sounds.ps1
    /// copia los suyos al compilar (o, si no está, los de Windows amplificados). ElementSoundPlayer no vale: usa otros
    /// sonidos, más bajos, y no pasa de su volumen 1. Se reproducen con AudioGraph (latencia mínima).
    /// </summary>
    internal static class Sounds
    {
        public static bool Enabled { get; set; }

        private static readonly string[] Names = { "Focus", "Invoke", "Show", "Hide", "GoBack" };

        // Los archivos de la app de Xbox vienen a volumen completo (pico 0 dBFS) y ella los reproduce más bajos.
        private const double Volume = 0.25;

        private static AudioGraph _graph;
        private static Dictionary<string, AudioFileInputNode> _nodes;
        private static Task _init;

        private static bool _watching;

        public static Task InitAsync()
        {
            if (!_watching)
            {
                // El grafo se queda en la salida que había al crearlo: al cambiar la predeterminada (auriculares,
                // Bluetooth…) se rehace para sonar por el mismo sitio que todo lo demás.
                _watching = true;
                Windows.Media.Devices.MediaDevice.DefaultAudioRenderDeviceChanged += (s, e) =>
                {
                    if (e.Role != Windows.Media.Devices.AudioDeviceRole.Default) return;
                    Reset();
                    var _ = InitAsync();
                };
            }
            return _init ?? (_init = InitCoreAsync());
        }

        private static async Task InitCoreAsync()
        {
            try
            {
                var settings = new AudioGraphSettings(AudioRenderCategory.SoundEffects)
                {
                    QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency,
                };
                var result = await AudioGraph.CreateAsync(settings);
                if (result.Status != AudioGraphCreationStatus.Success) { _init = null; return; }
                var graph = result.Graph;
                var output = await graph.CreateDeviceOutputNodeAsync();
                if (output.Status != AudioDeviceNodeCreationStatus.Success) { graph.Dispose(); _init = null; return; }

                var nodes = new Dictionary<string, AudioFileInputNode>();
                foreach (var name in Names)
                {
                    var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Sounds/" + name + ".wav"));
                    var input = await graph.CreateFileInputNodeAsync(file);
                    if (input.Status != AudioFileNodeCreationStatus.Success) continue;
                    input.FileInputNode.Stop();
                    input.FileInputNode.OutgoingGain = Volume;
                    input.FileInputNode.AddOutgoingConnection(output.DeviceOutputNode);
                    nodes[name] = input.FileInputNode;
                }
                // Si cambia la salida de audio (auriculares, Bluetooth…) el grafo deja de valer: se rehace al siguiente sonido.
                graph.UnrecoverableErrorOccurred += (s, e) => Reset();
                graph.Start();
                _graph = graph;
                _nodes = nodes;
            }
            catch (Exception)
            {
                _init = null;
            }
        }

        private static void Reset()
        {
            var graph = _graph;
            _graph = null;
            _nodes = null;
            _init = null;
            try { graph?.Dispose(); } catch (Exception) { }
        }

        public static void Focus() => Play("Focus");

        private static DateTime _lastBack;

        public static void Play(string name)
        {
            if (!Enabled) return;
            // Cerrar una lista con B ya suena como «atrás»: no se suma el de cerrar.
            if (name == "GoBack") _lastBack = DateTime.Now;
            else if (name == "Hide" && (DateTime.Now - _lastBack).TotalMilliseconds < 400) return;
            var nodes = _nodes;
            if (nodes == null) { var _ = InitAsync(); return; }
            AudioFileInputNode node;
            if (!nodes.TryGetValue(name, out node)) return;
            try
            {
                node.Seek(TimeSpan.Zero);
                node.Start();
            }
            catch (Exception)
            {
                Reset();
            }
        }
    }
}
