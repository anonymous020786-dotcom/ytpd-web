"""
Invoked directly via the standard Lambda Invoke API (not a Function URL -
new AWS accounts are placed under an anti-abuse restriction that blocks
public Function URL access even with AuthType=NONE or AWS_IAM; the plain
Invoke API isn't subject to that). The caller (a Cloudflare Worker) signs
the request with SigV4 using a narrowly-scoped IAM user's credentials
(ytpd-worker, lambda:InvokeFunction on just this function) - that IAM
auth is the entire trust boundary, not a shared secret.

Also confirmed by testing: on a brand-new AWS account, an identity-based
policy on the calling user alone was NOT enough (AccessDeniedException
even though IAM's own policy simulator said "allowed") - it additionally
needed an explicit resource-based policy on this function naming that
exact principal ARN (see infra/README.md). Apparently a defense-in-depth
requirement for new accounts, not documented anywhere I found; if you
hit the same mismatch (simulator says allowed, real call still denied),
this is why.

Starts the EC2 instance if it's stopped and returns its current state so
the Worker can show an appropriate "waking up" response.
"""
import os

import boto3

INSTANCE_ID = os.environ["INSTANCE_ID"]

ec2 = boto3.client("ec2")


def handler(event, context):
    resp = ec2.describe_instances(InstanceIds=[INSTANCE_ID])
    state = resp["Reservations"][0]["Instances"][0]["State"]["Name"]

    if state == "stopped":
        ec2.start_instances(InstanceIds=[INSTANCE_ID])
        state = "starting"

    return {"state": state}
