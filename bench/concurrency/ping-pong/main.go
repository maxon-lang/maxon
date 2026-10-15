package main

import (
	"fmt"

	"maxon.dev/bench/concurrency/internal/argv"
)

const defaultRoundTrips = 1000000

func partner(requests <-chan int, replies chan<- int) {
	for i := range requests {
		replies <- i + 1
	}
}

func main() {
	n := argv.Size(1, defaultRoundTrips)

	requests := make(chan int)
	replies := make(chan int)
	go partner(requests, replies)

	sum := 0
	for i := 0; i < n; i++ {
		requests <- i
		sum += <-replies
	}
	close(requests)

	fmt.Println("ping-pong", sum)
}
