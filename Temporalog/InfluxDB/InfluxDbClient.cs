using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Temporalog.Config;
using Vintagestory.API.Server;

namespace Temporalog.InfluxDB;

public class InfluxDbClient
{
    private readonly ICoreServerAPI _sapi;

    private readonly HttpClient _httpClient;

    private readonly string _writeEndpoint;
    
    private bool _isConnected;
    private bool _isReconnecting;

    public InfluxDbClient(TermporalogConfig config, ICoreServerAPI sapi)
    {
        _sapi = sapi;
        _writeEndpoint = $"write?org={config.Organization}&bucket={config.Bucket}";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{config.Url}/api/v2/")
        };

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {config.Token}");
    }

    internal void Dispose()
    {
        _httpClient.Dispose();
    }

    internal void WritePoint(PointData point, WritePrecision? precision)
    {
        if (!_isConnected) return;
        Task.Run(async () =>
        {
            try
            {
                if (precision != null)
                {
                    var httpResponseMessage = await _httpClient.PostAsync(
                        $"{_writeEndpoint}&precision={precision.ToString()?.ToLower()}",
                        new StringContent(point.ToLineProtocol(), Encoding.UTF8, "application/json"));
                    if (!httpResponseMessage.IsSuccessStatusCode)
                    {
                        var response = await httpResponseMessage.Content.ReadAsStringAsync();
                        _sapi.Logger.Warning($"[InfluxDB] {(int)httpResponseMessage.StatusCode} : {response}");
                        TryReconnect();
                    }
                }
                else
                {
                    var httpResponseMessage = await _httpClient.PostAsync(_writeEndpoint,
                        new StringContent(point.ToLineProtocol(), Encoding.UTF8, "application/json"));
                    if (!httpResponseMessage.IsSuccessStatusCode)
                    {
                        var response = await httpResponseMessage.Content.ReadAsStringAsync();
                        _sapi.Logger.Warning($"[InfluxDB] {(int)httpResponseMessage.StatusCode} : {response}");
                        TryReconnect();
                    }
                }
            }
            catch (Exception e)
            {
                _sapi.Logger.Warning($"[InfluxDB] {e}");
                TryReconnect();
            }
        });
    }

    internal void WritePoints(List<PointData> points, WritePrecision? precision)
    {
        if (!_isConnected) return;
        Task.Run(async () =>
        {
            try
            {
                var sb = new StringBuilder();
                for (var i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    sb.Append(point.ToLineProtocol());
                    if (i <= points.Count - 1)
                    {
                        sb.Append("\n");
                    }
                }

                if (precision != null)
                {
                    var httpResponseMessage = await _httpClient.PostAsync(
                        $"{_writeEndpoint}&precision={precision.ToString()!.ToLower()}",
                        new StringContent(sb.ToString(), Encoding.UTF8, "application/json"));
                    if (!httpResponseMessage.IsSuccessStatusCode)
                    {
                        var response = await httpResponseMessage.Content.ReadAsStringAsync();
                        _sapi.Logger.Warning($"[InfluxDB] {(int)httpResponseMessage.StatusCode} : {response}");
                        TryReconnect();
                    }
                }
                else
                {
                    var httpResponseMessage = await _httpClient.PostAsync(_writeEndpoint,
                        new StringContent(sb.ToString(), Encoding.UTF8, "application/json"));
                    if (!httpResponseMessage.IsSuccessStatusCode)
                    {
                        var response = await httpResponseMessage.Content.ReadAsStringAsync();
                        _sapi.Logger.Warning($"[InfluxDB] {(int)httpResponseMessage.StatusCode} : {response}");
                        TryReconnect();
                    }
                }
            }
            catch (Exception e)
            {
                _sapi.Logger.Warning($"[InfluxDB] {e}");
                TryReconnect();
            }
        });
    }

    public bool HasConnection()
    {
        try
        {
            var httpResponseMessage = _httpClient.GetAsync("orgs").GetAwaiter().GetResult();
            if (httpResponseMessage.IsSuccessStatusCode)
            {
                _sapi.Logger.Debug("Influxdb connected");
                _isConnected = true;
                return true;
            }

            _sapi.Logger.Error(
                $"Error connecting to {_httpClient.BaseAddress}. {httpResponseMessage.StatusCode}");
            return false;
        }
        catch (Exception e)
        {
            _sapi.Logger.Error(
                $"Could not connect to {_httpClient.BaseAddress}. {e.Message}");
            return false;
        }
    }

    public void TryReconnect()
    {
        if (_isReconnecting) return;

        _isConnected = false;
        _sapi.Logger.Notification(
            "Trying to reconnect in 10 sec");
        _isReconnecting = true;
        Task.Run(() =>
        {
            Thread.Sleep(10000);
            if (HasConnection())
            {
                _isReconnecting = false;
            }
            else
            {
                _isReconnecting = false;
                TryReconnect();
            }
        });
    }
}