"""Exercise the local simulator API. Start Equipment.Api before running this file."""

from concurrent.futures import ThreadPoolExecutor
import json
from threading import Barrier
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE = "http://127.0.0.1:5078"


def call(path, payload=None):
    body = None if payload is None else json.dumps(payload).encode()
    request = Request(BASE + path, data=body, headers={"Content-Type": "application/json"})
    try:
        response = urlopen(request, timeout=15)
    except HTTPError as error:
        response = error
    with response:
        return response.status, json.load(response)


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    require(call("/health") == (200, {"status": "ready"}), "Health check failed.")
    status, data = call("/batches", {"wafers": 12, "chambers": 4, "faultWafer": 3})
    require(status == 200, "Batch failed.")
    require(1 < data["peakActiveChambers"] <= 4, "Unexpected active chamber count.")
    require([result["wafer"] for result in data["wafers"]] == list(range(1, 13)),
            "Wafer results are missing or out of order.")
    require(data["wafers"][2]["stages"] == ["Idle", "Loaded", "Processing", "Faulted"],
            "Unexpected fault path.")
    require(data["wafers"][2]["alarm"] == "SIM_SENSOR_TIMEOUT", "Missing simulated alarm.")
    require(all(result["stages"][-1] == "Completed" for result in data["wafers"]
                if result["wafer"] != 3), "A neighboring wafer failed.")
    require(call("/batches", {"wafers": 0, "chambers": 4})[0] == 400, "Expected HTTP 400.")
    print("PASS: health, batch response, injected fault, and input rejection")

    ready = Barrier(2)

    def submit():
        ready.wait(timeout=5)
        return call("/batches", {"wafers": 28, "chambers": 1})[0]

    with ThreadPoolExecutor(max_workers=2) as pool:
        requests = [pool.submit(submit) for _ in range(2)]
        statuses = sorted(request.result() for request in requests)
    require(statuses == [200, 409], f"Expected one accepted request and one conflict, got {statuses}.")
    require(call("/batches", {"wafers": 1, "chambers": 1})[0] == 200, "Batch slot did not reopen.")
    print("PASS: concurrent submission conflict and slot recovery")


if __name__ == "__main__":
    main()
