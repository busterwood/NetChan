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

# Performance

The following show the results of a simple `int` channel producer and consumer on an Intel i7-2600S

Test            | Test ops   | ops/sec   | ns/op | User Secs | Kernal Secs 
----------------|------------|-----------|-------|-----------|--------------
unbuffered      | 300,000    | 224,810   | 4448  | 0.33      |  0.23
queue size 10   | 10,000,000 | 7,995,875 |  125  | 1.47      | 0.16
queue size 100  | 20,000,000 | 7,930,553 |  126  | 2.23 | 0.34
queue size 1000 | 20,000,000 | 8,155,971 |  122  | 3.48 | 0.11
select on two channels   | 3,000,000  | 1,014,302 | 985 | 3.09 | 1.06
unbuffered select        | 1,000,000  | 465,937   | 2146 | 0.78 | 1.28
select on buffer of 1    | 2,000,000  | 673,919   | 1483 | 0.66 | 0.39
select on buffer of 10   | 2,000,000  | 658,779   | 1517 | 0.86 | 0.37
select on buffer of 100  | 10,000,000 | 4,412,083 | 226 | 3.09 | 0.27
select on buffer of 1000 | 10,000,000 | 3,707,186 | 269 | 3.85 | 0.08
