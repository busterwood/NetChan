# NetChan
Go-like channels for .NET

The `Channel<T>` class is like an unbuffered Go channel, so can be used to synchronise threads.
The `QueuedChannel<T>` class is like an buffered Go channel, it has a fixed capacity queue of items.

All channels supports:
* `Send(T)` which may block if a receiver is not ready (or the queue is full for `QueuedChannel<T>`)
* `TrySend(T)` which only sends if a reciver is ready (or the queue is _not_ full for `QueuedChannel<T>`)
* `Recv()` which may block if a sender is not ready (or the queue is empty for `QueuedChannel<T>`)
* `TryRecv(T)` which only recives if a sender is ready (or the queue is _not_ empty for `QueuedChannel<T>`)

Also supports blocking recieves over many channels via the `Select` class, which is a bit like a Go select statement.
