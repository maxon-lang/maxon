package main

import (
	"fmt"
	"sync"

	"maxon.dev/bench/concurrency/internal/argv"
)

const (
	incrementerCount      = 64
	defaultPerIncrementer = 100000
)

type counter struct {
	mu    sync.Mutex
	count int
}

func (c *counter) inc() {
	c.mu.Lock()
	c.count++
	c.mu.Unlock()
}

func (c *counter) read() int {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.count
}

func increment(c *counter, perIncrementer int, wg *sync.WaitGroup) {
	defer wg.Done()

	for j := 0; j < perIncrementer; j++ {
		c.inc()
	}
}

func main() {
	perIncrementer := argv.Size(1, defaultPerIncrementer)

	var c counter
	var wg sync.WaitGroup
	wg.Add(incrementerCount)
	for i := 0; i < incrementerCount; i++ {
		go increment(&c, perIncrementer, &wg)
	}
	wg.Wait()

	fmt.Println("shared-counter", c.read())
}
