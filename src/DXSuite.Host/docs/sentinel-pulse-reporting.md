# Sentinel Pulse Reporting

Sentinel can report device health into a customer's existing Pulse database.
For each device linked to a Pulse device, it keeps `tblDevice.Heartbeat`
current and writes a `tblPulse` reading whenever the device's open issues
change. Pulse then raises its own down alerts from the heartbeat.

It is off by default. An upgrade never starts writing into a live Pulse until
an operator turns it on for the tenant.

The design is `docs/spec/sentinel-pulse-reporting.md` in the repository.

## What Sentinel writes

- `tblDevice.Heartbeat` for linked devices with fresh evidence. It is never
  later than the current time.
- `tblPulse` readings, with an explicit `ErrorLevel` of `'0'` to `'4'` and
  `LastEditUser = 'Sentinel'`. A cleared issue writes `'0'`.
- `tblDevice` rows, only when an operator uses Create in Pulse.
- `tblDevice.Snooze` and `SnoozeTime`, only from the Snooze controls.
  `SnoozeTime` is written in UTC.
- `tblDevice.Monitor`, only from **Start monitoring** and **Stop monitoring**
  in a device's inspector. Start also writes a fresh heartbeat, and is refused
  while Sentinel cannot vouch for the device.

It never writes `tblAlert`, `tblMonitoritem` or `tblDeviceHistory`, and never
deletes anything. The publisher itself never changes `tblDevice.Monitor`.

## Configuration

```json
"Sentinel": {
  "PulseReporting": {
    "Enabled": false,
    "CustomerId": "",
    "CycleSeconds": 60,
    "ReassertHours": 24
  }
}
```

- Each key is read from `Tenants:<subdomain>:Sentinel:PulseReporting` first,
  then from the top-level `Sentinel:PulseReporting`.
- The Pulse connection string is `Tenants:<subdomain>:ConnectionStrings:Pulse`,
  falling back to the top-level `ConnectionStrings:Pulse`.
- `CustomerId` is the Pulse `CustomerID` every row is written under.
- `ReassertHours` rewrites still-open issues so they outlive Pulse's reading
  retention.
- If `Enabled` is true and `CustomerId` or the connection string is missing,
  the site stops about 15 seconds after it starts, and the log names the
  setting. That is deliberate: otherwise it would report nowhere.

Issue levels and monitor items are data, not configuration. Edit them on
**Configuration > Pulse Reporting**.

## Permissions the Pulse login needs

The customer's DBA applies these to the login in the Pulse connection string.
The deployment never runs them.

```sql
GRANT SELECT ON dbo.tblDevice       TO [<sentinel login>];
GRANT SELECT ON dbo.tblDeviceType   TO [<sentinel login>];
GRANT SELECT ON dbo.tblMonitoritem  TO [<sentinel login>];
GRANT SELECT ON dbo.tblCustomer     TO [<sentinel login>];
GRANT INSERT ON dbo.tblPulse        TO [<sentinel login>];
GRANT INSERT ON dbo.tblDevice       TO [<sentinel login>];
GRANT UPDATE (Heartbeat, Snooze, SnoozeTime, Monitor) ON dbo.tblDevice TO [<sentinel login>];
```

Table names follow the live Pulse spelling (`tblMonitoritem`, lower-case `i`).

Without the `Monitor` column in the UPDATE grant, Start and Stop monitoring
show "The Pulse login is not allowed to change Monitor". Everything else keeps
working.

## Release notes

- **Start and stop Pulse monitoring from Monitored Devices (DXME-284).** The
  grant above now includes `Monitor`. On existing customers, the DBA must
  re-apply the `GRANT UPDATE` line before the toggle works.

## Upgrade

Run `deploy/sentinel-upgrade-<version>.sql` against the tenant's Sentinel
database and `deploy/director-upgrade-<version>.sql` against its Director
database. Each script lists its before-running steps in its header. Neither
touches Pulse.

## Day one

1. Have the customer's DBA apply the grants above.
2. Set `CustomerId`, the Pulse connection string and `Enabled: true` for the
   tenant, then restart the site.
3. Open **Configuration > Monitored Devices**, tick the devices to report, and
   use the bulk inspector. For each device choose **Link to** the suggested
   Pulse device, **Create as** a new one, or **Skip**, then press **Apply**.
4. Run the query in the next section and give the result to a Pulse admin. Do
   not delete anything from Sentinel.
5. Attach URL monitors to their reporting devices from each device's inspector.
   Work down **Show unattached URL monitors** on Monitored Devices until it is
   gone.
6. Adjust levels and monitor items on **Pulse Reporting** if the defaults do
   not suit.

The status line on Monitored Devices shows the last successful publish and how
many linked devices are failing. A device whose Pulse row has gone shows
**Link broken** in its inspector and is not written to again until someone
presses **Retry**.

## Devices the retired sink created

Before this feature, `PulseAlertSink` (shipped 2026-08-04) inserted Pulse
devices with `Monitor = 1` and never heartbeated them. Sentinel cannot tell
them from real devices that share the id, so it leaves them alone. Run this
against the Pulse database with the tenant's `CustomerId`, and let a Pulse admin
decide what to keep:

```sql
DECLARE @CustomerId varchar(20) = '<CustomerId>';

SELECT d.DeviceID, d.Description, d.CreateDate, d.Monitor,
       (SELECT COUNT(*) FROM dbo.tblPulse p
         WHERE p.DeviceID = d.DeviceID AND p.CustomerID = d.CustomerID
           AND p.MonitorItemID = 0) AS DeviceItemReadings
FROM dbo.tblDevice d
WHERE d.CustomerID = @CustomerId
  AND d.Heartbeat IS NULL
  AND d.Monitor = 1
  AND d.MonitorTemplateID = 1
  AND d.CreateDate >= '2026-08-04'
ORDER BY d.CreateDate;
```

## Troubleshooting

- **Pulse unreachable** on Monitored Devices means the site cannot read Pulse.
  Status, linking and snooze wait for it. Publishing carries on and retries each
  cycle; nothing is queued.
- A device that stops reporting drops out of the heartbeat within 5 minutes.
  Pulse lists it in `View_Devices_Down` about 6 minutes after its last
  heartbeat.
- A monitor item that no longer exists in Pulse fails every write for that
  item. Pick an existing item on Pulse Reporting.
