package main

import (
	"fmt"

	"maxon.dev/bench/concurrency/internal/argv"
)

const (
	incrementerCount       = 64
	defaultPerIncrementer  = 100000
	mailboxChannelCapacity = 1024
)

type operation int

const (
	incrementOperation operation = iota
	readOperation
)

type message struct {
	operation operation
	reply     chan<- int
}

func count(mailbox <-chan message) {
	total := 0
	for m := range mailbox {
		switch m.operation {
		case incrementOperation:
			total++
		case readOperation:
			m.reply <- total
		default:
			panic(fmt.Sprintf("shared-counter: unknown operation %d", m.operation))
		}
	}
}

func increment(mailbox chan<- message, perIncrementer int, done chan<- int) {
	for j := 0; j < perIncrementer; j++ {
		mailbox <- message{operation: incrementOperation}
	}
	done <- perIncrementer
}

func main() {
	perIncrementer := argv.Size(1, defaultPerIncrementer)

	mailbox := make(chan message, mailboxChannelCapacity)
	go count(mailbox)

	finished := make([]chan int, incrementerCount)
	for i := range finished {
		finished[i] = make(chan int, 1)
		go increment(mailbox, perIncrementer, finished[i])
	}

	for _, done := range finished {
		<-done
	}

	reply := make(chan int)
	mailbox <- message{operation: readOperation, reply: reply}
	fmt.Println("shared-counter", <-reply)
	close(mailbox)
}
