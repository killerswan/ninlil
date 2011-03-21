// Copyright (c) Kevin Cantu <me@kevincantu.org>
//
// redux - manipulate old Tumblr posts


// dependencies /////////////////////////////////////////////

open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing


// command line args /////////////////////////////////////////////

let ( api, email, password ) = match System.Environment.GetCommandLineArgs() with
                               | [| _; blog; email; password |] -> 
                                    ( ("http://" + blog + ".tumblr.com/api"), email, password )
                               | _ -> 
                                    failwithf "Usage: mono exe BLOG EMAIL PASSWORD"


// fetch a URL /////////////////////////////////////////////

let getDocRaw (url:string) : string = 
   // This function doesn't return, at all, if the URL is wrong 
   // in some ways. TODO: fix. 

   let getpage (url:string) = 
      async {
         // see Expert F# at 383, etc.
         let req        = WebRequest.Create(url, Timeout=5)
         use! response  = req.AsyncGetResponse()
         use reader     = new StreamReader(response.GetResponseStream())
         return reader.ReadToEnd()
      }

   // get data as XML, return it
   (Async.RunSynchronously(getpage url))


// simple queries /////////////////////////////////////////////

// xml out
let readPosts (start:int) (num:int)       = getDocRaw <| api + "/read" +
                                                         "?start=" + (sprintf "%d" start) + 
                                                         "&num="   + (sprintf "%d" num) +
                                                         "&type="  + "photo"

// status out
let deletePost (id:string)                = getDocRaw <| api + "/delete" +
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id
                     
// new id out
let reblogPost (id:string) (rkey:string)  = getDocRaw <| api + "/reblog" + 
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id + 
                                                         "&reblog-key=" + rkey


// process XML results /////////////////////////////////////////////

// after getPosts
let processPosts postsXML =
   let doc = new XmlDocument()
   postsXML |> doc.LoadXml // so doc is mutable?

   // add prettier printing
   //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

   // process the data
   let tumblr = doc.ChildNodes.Item(1)
   let posts  = tumblr.ChildNodes.Item(1)

   // overall statistics
   let start = System.Convert.ToInt32(posts.Attributes.GetNamedItem("start").Value)
   let total = System.Convert.ToInt32(posts.Attributes.GetNamedItem("total").Value)
   let num   = posts.ChildNodes.Count

   // posts.HasChildNodes
   let postsFound = 
      [
         for ii in 0..(num-1) do
            let post       = posts.ChildNodes.Item(ii)
            let id         = post.Attributes.GetNamedItem("id").Value
            let reblogkey  = post.Attributes.GetNamedItem("reblog-key").Value
            let date       = post.Attributes.GetNamedItem("date-gmt").Value
            yield (id, reblogkey, date, post)
      ]

   // display a post tuple
   let display (id, reblogkey, date, post:XmlNode) = 
      let pic = post.ChildNodes.Item(1).InnerText
      printfn "id: %s, %s, %s -> %s" id reblogkey date pic

   (*
   // print stats
   printfn "%d of %d, starting at %d" num total start

   // print all
   postsFound |> List.map display |> ignore
   *)

   (start, total, postsFound)


// compose read and process /////////////////////////////////////////////
let readAndProcPosts a b = readPosts a b |> processPosts


// dates /////////////////////////////////////////////

// "2010-11-24 05:57:26 GMT" -> System.DateTime
let processDate (datestring: string) =
   (System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )) 

// date of post
let dateOfPost (index: int) : System.DateTime = 
   let (start, total, posts) = readPosts index 1 |> processPosts

   // srsly, TODO: make this post tuple a type
   let (_,_,datestring,_) = posts |> List.head 

   (processDate datestring)


// agents /////////////////////////////////////////////

let reader = 
   MailboxProcessor.Start(
      fun inbox ->
         let rec loop() = 
            inbox.Scan(
               function 
               | ((start: int), (count: int)) -> 
                  Some(async { 
                                 readPosts start count 
                                 |> processPosts 
                                 |> ignore
                                 return! loop() 
                  })
               //| _ -> None
            )
         loop()
      )


// range to consider ////////////////////////////////

// get the most recent post on a given date
let rangeEndingIn (targetDate: System.DateTime) : int*int = 

   // if we have a match for the right date, step the the latest post on that date
   let rec walkToNewestMatch (start: int) (target: System.DateTime) : int =
      let nextPostDate = dateOfPost (start-1)
      match (nextPostDate > target) with
      | true  -> start
      | false -> walkToNewestMatch (start-1) target
      
   // binsearch to find latest post before a given date
   let rec findCutoff (target: System.DateTime) (newest: int) (oldest: int) : int = 

      let middle = (newest + oldest) / 2

      let middleDate : System.DateTime = dateOfPost middle

      match (middleDate, target) with
      | a,b when newest =  oldest && a < b -> middle
      | a,b when newest =  oldest && a = b -> walkToNewestMatch middle target
      | a,b when newest =  oldest && a > b -> middle+1
      | a,b when newest <> oldest && a < b -> findCutoff target newest (middle-1)
      | a,b when newest <> oldest && a = b -> walkToNewestMatch middle target    // combine above
      | a,b when newest <> oldest && a > b -> findCutoff target (middle+1) oldest

   // get the latest post
   let (startingPostNumber, total, posts) = readPosts 1 1 |> processPosts

   // find where the end of the date we care about is
   let first = total
   let last = findCutoff targetDate startingPostNumber total

   (first, last)


// tests /////////////////////////////////////////////

let testPostReblogging() = 
   // read some posts
   let (start, total, posts) = readPosts 6666 4 |> processPosts

   // reblog those and delete original posts
   (posts |> List.map (fun (id, rkey, datestring, post) ->
         reblogPost id rkey   |> ignore
         deletePost id        |> ignore

         let date = processDate datestring

         (date.Year, date.Month)))


(*
let testFindingCutoff() = 
   printfn ""

   let p1 = cutoff (System.DateTime(2010,12,31))
   printfn "The latest post on or before that date is #%d" p1
   printfn ""

   let p2 = cutoff (System.DateTime(2011,7,8))
   printfn "The latest post on or before that date is #%d" p2
   printfn ""

   readPosts 1115 5 |> processPosts |> ignore
*)


let testReaderAgent() =
   reader.Post (1,5) |> ignore






