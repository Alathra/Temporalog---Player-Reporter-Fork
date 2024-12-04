# Influx + Grafana

Termporalog adds logging of server metrics, to use this feature, you'll need an influxdb and grafana service running. Here are instructions on how to spin them up using docker-compose.

## Docker Compose configuration

The InfluxDB logger logs every 10 seconds to influxDB except player deaths, login/logout and warning/errors those are logged as they are encountered. Since the general server metrics are only logged every 10 seconds it might miss some spikes or dips in the metrics, keep that in mind.

For an example install using docker you can use the docker-compose.yaml.

- [Docker Engine Install](https://docs.docker.com/engine/install/)
- [Docker-Compose Install](https://docs.docker.com/compose/install/)
- [docker-compose.yaml](https://gitlab.com/th3dilli_vintagestory/temporalog/-/tree/main/influx_grafana)
  change the path to the volumes if you wish so

if you wanna use docker and run the vs server in pterodactyl they need to be on the same network see [docker-compose.yaml](https://gitlab.com/th3dilli_vintagestory/temporalog/-/tree/main/influx_grafana)

Both InfluxDB and Grafana allow you to configure it thorugh the Webinterface when not initilized yet.

If you need help or want to manually here are some useful links.\
Make sure to persist your influxdb data with docker volumes/mounts.

- [InfluxDB Docs](https://docs.influxdata.com/influxdb/v2.1/)
- [Grafana Docs](https://grafana.com/docs/grafana/latest/installation/?pg=docs)

Edit `.env` file to set docker volumes paths and port mappings:

`INFLUXDB_PORT` - port to access influxdb web UI
`INFLUXDB_DATA` - path to store influxdb data (local volume)
`INFLUXDB_CONFIG` - path to store influxdb config (local volume)
`GRAFANA_PORT` - port to access grafana web UI
`GRAFANA_DATA` - path to store grafana data (local volume)

## Run the services

To start the services:

```
docker compose up -d
```

You can now check the services status by inspecting docker containers (`docker ps`)

## Influx DB configuration

1. Login to InfluxDB web UI (by default `http://localhost:8086`), create the account.
2. For bucket name choose `serverstats`, otherwise you'll need to modify grafana dashboard json.
3. Get API token from `Data -> API Tokens` - you'll need it for `TermporalogConfig.json` and grafana

## Grafana configuration

Login to grafana web UI (by default `http://localhost:3000`) using default credentials `admin/admin`

### Data source configuration

1. Go to `Connections -> Data Sources`
2. Click the `Add new data source` Button
3. Choose `InfluxDB`:

- Set `Query Language` to `Flux`
- Set `URL` to `http://influxdb:8086` (or other port set in `$INFLUXDB_PORT`)
- Set `Organization` to the one set in account creation stage
- Set `Token` to the influxdb API Token
- Click 'Save & test'

### Dashboard configuration

1. Go to `Dashboards`
2. Click `New` -> `Import`
3. Paste the contents of `VS_Metrics_Dashboard_Grafana.json` and click 'Load'


### Sample Config

```json5
{
  // Url of your InfluxDB isntallation
  "Url": "http://localhost:8086",
  // API Toekn for InfluxDB needs write acces to the bucket 
  "Token": "",
  // The bucket to write the date to , you can leave this if you followed the above setup instructions
  "Bucket": "serverstats",
  // set the name of the organisation you choose at account creation
  "Organization": "orgname",
  // If set to true it will not print the Logticks in the server log
  "OverwriteLogTicks": false,
  // interval for the data to be collected in ms (10000ms == 10s default)
  // player ping is always collected in 10s interval regardless of this setting, so the server play time to be accurate
  "DataCollectInterval": 10000
}
```