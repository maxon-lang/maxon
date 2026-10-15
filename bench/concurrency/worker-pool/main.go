package main

import (
	"fmt"
	"runtime"
	"sync"

	"maxon.dev/bench/concurrency/internal/argv"
)

const (
	defaultTaskCount  = 4096
	defaultIterations = 250000
)

func kernel(task int, iterations int) uint64 {
	x := uint64(task) + 1
	for k := 0; k < iterations; k++ {
		x ^= x << 13
		x ^= x >> 7
		x ^= x << 17
	}
	return x >> 32
}

func work(iterations int, jobs <-chan int, results chan<- uint64, wg *sync.WaitGroup) {
	defer wg.Done()

	for task := range jobs {
		results <- kernel(task, iterations)
	}
}

func main() {
	taskCount := argv.Size(1, defaultTaskCount)
	iterations := argv.Size(2, defaultIterations)
	workerCount := runtime.GOMAXPROCS(0)

	jobs := make(chan int, taskCount)
	results := make(chan uint64, taskCount)

	var wg sync.WaitGroup
	wg.Add(workerCount)
	for w := 0; w < workerCount; w++ {
		go work(iterations, jobs, results, &wg)
	}

	for task := 0; task < taskCount; task++ {
		jobs <- task
	}
	close(jobs)

	wg.Wait()
	close(results)

	var sum uint64
	for r := range results {
		sum += r
	}

	fmt.Println("worker-pool", sum)
}
