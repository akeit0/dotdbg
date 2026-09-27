# AsyncDispatch test fixture

This small app supplies async exceptions, retry catches, and user output for the live debugger tests. Set `DISPATCH_FAIL_ONCE=J200` to make the first J200 gateway call throw. `--retries` counts additional attempts, so `--retries 0` exercises the terminal catch and `--retries 1` recovers.
