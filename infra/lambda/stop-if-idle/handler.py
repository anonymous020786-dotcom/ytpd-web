"""
Runs on an EventBridge schedule (every ~10 min). Stops the EC2 instance
if it's been running with near-zero inbound network traffic for the
configured idle window - a fully AWS-native activity signal, so this
doesn't depend on anything outside AWS (no Cloudflare KV/API calls,
fewer moving parts, fewer places this can silently break).
"""
import os
from datetime import datetime, timedelta, timezone

import boto3

INSTANCE_ID = os.environ["INSTANCE_ID"]
IDLE_MINUTES = int(os.environ.get("IDLE_MINUTES", "20"))
# ~500KB over the whole window - real traffic (page loads, SignalR,
# downloads) is far above this; an idle box's background chatter is not.
IDLE_THRESHOLD_BYTES = int(os.environ.get("IDLE_THRESHOLD_BYTES", "500000"))

ec2 = boto3.client("ec2")
cw = boto3.client("cloudwatch")


def handler(event, context):
    resp = ec2.describe_instances(InstanceIds=[INSTANCE_ID])
    instance = resp["Reservations"][0]["Instances"][0]
    state = instance["State"]["Name"]

    if state != "running":
        return {"state": state, "action": "none"}

    now = datetime.now(timezone.utc)
    launch_time = instance["LaunchTime"]
    # Grace period: don't stop an instance that just started - give it
    # time to actually boot and serve the request that woke it up.
    if (now - launch_time) < timedelta(minutes=5):
        return {"state": state, "action": "none", "reason": "grace period"}

    start = now - timedelta(minutes=IDLE_MINUTES)
    metrics = cw.get_metric_statistics(
        Namespace="AWS/EC2",
        MetricName="NetworkIn",
        Dimensions=[{"Name": "InstanceId", "Value": INSTANCE_ID}],
        StartTime=start,
        EndTime=now,
        Period=IDLE_MINUTES * 60,
        Statistics=["Sum"],
    )
    total_bytes = sum(dp["Sum"] for dp in metrics["Datapoints"]) if metrics["Datapoints"] else 0

    if total_bytes < IDLE_THRESHOLD_BYTES:
        ec2.stop_instances(InstanceIds=[INSTANCE_ID])
        return {"state": state, "action": "stopping", "network_in_bytes": total_bytes}

    return {"state": state, "action": "none", "network_in_bytes": total_bytes}
