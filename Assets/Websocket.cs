using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

// Plain TCP server (not an actual WebSocket) fed by the DYNAMO core's unity node, one message per read in
// request/response lockstep. Messages:
//   state: <x> <y> <closeL> <closeR> <scores...> <manual_id> <silly 0|1> <brightness 0..100>
//          scores are 13 in DYNAMO core 3.0 (angry, disgusted, happy, neutral, sad, surprised, hypnotic, heart,
//          rainbow, nightmare, gears, sans, mischievous) or 7 in the v1 format (first six, then mischievous)
//   AUDIO <voice_level> <speaking 0|1> <vowel A/I/U/E/O or -> <music_energy> <music_peak 0|1>
//   VIDEO PLAY <url> | VIDEO STOP
public class Websocket : MonoBehaviour
{
    private const int port = 50000;
    private const int LEGACY_SCORE_COUNT = 7;
    private const int MANUAL_SPECIAL_FIRST = 6;    // manual ids 6..11 = hypnotic, heart, rainbow, nightmare, gears, sans
    private const int SCORE_MISCHIEVOUS = 12;
    private TcpListener listener;

    public Slider xSlider, ySlider, leftEyeClosenessSlider, rightEyeClosenessSlider,
        angrySlider, disgustedSlider, happySlider, neutralSlider, sadSlider, surprisedSlider,
        hypnoticSlider, heartSlider, rainbowSlider, nightmareSlider, gearsSlider, sansSlider, mischievousSlider,
        brightnessSlider;
    public Toggle sillyMode;
    public VideoPlayerController videoPlayerController;

    private static float ParseFloat(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static bool AllNumeric(string[] terms, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!float.TryParse(terms[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { return false; }
            if (value < -1) { return false; }
        }
        return true;
    }

    private bool ValidateMessage(string[] terms)
    {
        if (terms[0] == "VIDEO") { return terms.Length >= 2 && terms.Length <= 3; }
        if (terms[0] == "AUDIO") { return terms.Length == 6 && AllNumeric(terms, 1, 3) && AllNumeric(terms, 4, 6); }
        if (terms.Length < 4 + LEGACY_SCORE_COUNT + 3) { return false; } // 4 eye values + at least the 7 legacy scores + manual id + silly + brightness
        return AllNumeric(terms, 0, terms.Length);
    }

    private void ApplyState(string[] terms)
    {
        int n = terms.Length;
        int scoreCount = n - 7;
        float Score(int i) => i < scoreCount ? ParseFloat(terms[4 + i]) : 0f;
        xSlider.value = (ParseFloat(terms[0]) + 1) / 2 * (xSlider.maxValue - xSlider.minValue) + xSlider.minValue;
        ySlider.value = (ParseFloat(terms[1]) + 1) / 2 * (ySlider.maxValue - ySlider.minValue) + ySlider.minValue;
        leftEyeClosenessSlider.value = ParseFloat(terms[2]);
        rightEyeClosenessSlider.value = ParseFloat(terms[3]);
        angrySlider.value = Score(0);
        disgustedSlider.value = Score(1);
        happySlider.value = Score(2);
        neutralSlider.value = Score(3);
        sadSlider.value = Score(4);
        surprisedSlider.value = Score(5);
        mischievousSlider.value = scoreCount == LEGACY_SCORE_COUNT ? Score(6) : Score(SCORE_MISCHIEVOUS);
        int manualExpression = Mathf.RoundToInt(ParseFloat(terms[n - 3]));
        sillyMode.isOn = Mathf.RoundToInt(ParseFloat(terms[n - 2])) == 1;
        brightnessSlider.value = ParseFloat(terms[n - 1]) / 100f;
        hypnoticSlider.value = manualExpression == MANUAL_SPECIAL_FIRST ? 1 : 0;
        heartSlider.value = manualExpression == MANUAL_SPECIAL_FIRST + 1 ? 1 : 0;
        rainbowSlider.value = manualExpression == MANUAL_SPECIAL_FIRST + 2 ? 1 : 0;
        nightmareSlider.value = manualExpression == MANUAL_SPECIAL_FIRST + 3 ? 1 : 0;
        gearsSlider.value = manualExpression == MANUAL_SPECIAL_FIRST + 4 ? 1 : 0;
        sansSlider.value = manualExpression == MANUAL_SPECIAL_FIRST + 5 ? 1 : 0;
    }

    private static void ApplyAudio(string[] terms)
    {
        AudioFeed.Set(
            ParseFloat(terms[1]),
            Mathf.RoundToInt(ParseFloat(terms[2])) == 1,
            terms[3].Length > 0 ? terms[3][0] : '-',
            ParseFloat(terms[4]),
            Mathf.RoundToInt(ParseFloat(terms[5])) == 1);
    }

    private async Task StartListening()
    {
        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            HandleClient(client);
        }
    }

    private async void HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[1024];
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) { break; } // client disconnected
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    string[] terms = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string response;
                    if (terms.Length > 0 && ValidateMessage(terms))
                    {
                        response = "Acknowledged: " + message;
                        if (terms[0] == "VIDEO")
                        {
                            if (terms[1] == "PLAY" && terms.Length == 3)
                            {
                                Debug.Log("VIDEO PLAY " + terms[2]);
                                videoPlayerController.PlayVideoWithSoundOnce(terms[2]);
                            }
                            else
                            {
                                Debug.Log("VIDEO STOP");
                                videoPlayerController.StopVideo();
                            }
                        }
                        else if (terms[0] == "AUDIO") { ApplyAudio(terms); }
                        else { ApplyState(terms); }
                    }
                    else { response = "Invalid message format!"; }
                    byte[] responseMessage = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseMessage, 0, responseMessage.Length);
                }
            }
        }
        catch (Exception e) when (e is System.IO.IOException || e is ObjectDisposedException || e is SocketException)
        {
            Debug.LogWarning("Unity node connection closed: " + e.Message);
        }
    }

    private async void Start()
    {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Debug.Log("WebSocket server started on localhost:" + port);
            await StartListening();
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to start WebSocket server: " + e.Message);
        }
    }

    private void OnApplicationQuit()
    {
        if (listener != null)
        {
            listener.Stop();
            Debug.Log("WebSocket server stopped");
        }
    }
}
