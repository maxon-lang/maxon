package main

import (
	"fmt"
	"sync"

	"maxon.dev/bench/concurrency/internal/argv"
)

const (
	producerCount       = 64
	defaultPerProducer  = 100000
	sinkChannelCapacity = 1024
)

func produce(p int, perProducer int, sink chan<- int, wg *sync.WaitGroup) {
	defer wg.Done()

	for j := 0; j < perProducer; j++ {
		sink <- p*perProducer + j
	}
}

func consume(sink <-chan int, total chan<- int) {
	sum := 0
	for v := range sink {
		sum += v
	}
	total <- sum
}

func main() {
	perProducer := argv.Size(1, defaultPerProducer)

	sink := make(chan int, sinkChannelCapacity)
	total := make(chan int)
	go consume(sink, total)

	var wg sync.WaitGroup
	wg.Add(producerCount)
	for p := 0; p < producerCount; p++ {
		go produce(p, perProducer, sink, &wg)
	}
	wg.Wait()
	close(sink)

	fmt.Println("fan-in", <-total)
}
