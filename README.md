# NetChan
Go-like channels for .NET

Calling `new Channel<T>()` creates an unbuffered Go channel, so can be used to synchronise threads.
Calling `new Channel<T>(capacity)` creates an buffered channel with a fixed capacity queue of items.

Supported channels operations are:
* `Send(T)` which may block if a receiver is not ready (or the queue is full for `QueuedChannel<T>`)
* `TrySend(T)` which does _not_ block and only sends if a reciever is ready (or the buffer is _not_ full for buffered channels)
* `Recv()` which may block if a sender is not ready (or the queue is empty for `QueuedChannel<T>`)
* `TryRecv(T)` which does _not_ block and only recieves if a sender is ready (or the buffer is _not_ empty for buffered channels)
* `Close()` which tell recieves that no further messages will be sent

`Channel<T>` are also `Enumerable<T>`, so you can easily read all the items using a `foreach`.  The enumeration finishes when the channel is closed via `Close()`.

Recieves over one of many channels is support via the `Select` class, which is a bit like a Go select statement.
`Select` supports:
* `Recv()` which may block if no sender is ready
* `TryRecv(T)` which does _not_ block and only recieves if a sender is ready
