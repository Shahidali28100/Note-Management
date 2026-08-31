// Integration tests share a LocalDB database and each ClassInitialize/[TestMethod] migrates it —
// running them in parallel races on CREATE DATABASE / migration history. Unlike the Unit test
// project, this assembly must run sequentially.
[assembly: DoNotParallelize]
